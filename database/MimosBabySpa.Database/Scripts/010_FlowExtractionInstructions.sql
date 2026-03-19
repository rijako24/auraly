-- =============================================================================
-- Migration 010: Add extractionInstructions to Mimo's Baby Spa flow definition
-- Adds generic extraction context (date resolution, time normalization, catalog
-- lookup rules) as flow-level configuration. The engine resolves {{runtime.*}}
-- tokens at extraction time — no domain logic lives in the engine itself.
-- =============================================================================

BEGIN TRANSACTION;

-- Patch the existing DefinitionJson to inject extractionInstructions + updated
-- engineSettings (extractionMaxTokens, extractionMinConfidence, maxConversationHistoryMessages=6).
-- We use JSON_MODIFY to surgically update the existing record without re-inserting.

DECLARE @FlowDefId UNIQUEIDENTIFIER;

SELECT TOP 1 @FlowDefId = fd.FlowDefinitionId
FROM [dbo].[FlowDefinitions] fd
INNER JOIN [dbo].[Agents] a ON fd.AgentId = a.AgentId
WHERE a.Name = 'Mimo Bot' AND fd.IsActive = 1;

IF @FlowDefId IS NULL
BEGIN
    RAISERROR('FlowDefinition for Mimo Bot not found. Run 009_GenericFlowEngine.sql first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ── 1. Update engineSettings (add new fields, fix maxConversationHistoryMessages) ──

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = JSON_MODIFY(
      JSON_MODIFY(
        JSON_MODIFY(
          [DefinitionJson],
          '$.engineSettings.extractionMaxTokens', 600),
        '$.engineSettings.extractionMinConfidence', 0.6),
      '$.engineSettings.maxConversationHistoryMessages', 6)
WHERE [FlowDefinitionId] = @FlowDefId;

-- ── 2. Add extractionInstructions ─────────────────────────────────────────────
-- Note: JSON_MODIFY adds the property if it doesn't exist.

DECLARE @ExtractionInstructions NVARCHAR(MAX) = N'Contexto temporal:
- Hoy: {{runtime.today}} ({{runtime.day_of_week}})
- Mañana: {{runtime.tomorrow}}
- Pasado mañana: {{runtime.day_after_tomorrow}}
- Hora actual: {{runtime.current_time}}

Reglas de fechas (mapeo obligatorio):
- "hoy" → {{runtime.today}}
- "mañana" → {{runtime.tomorrow}}
- "pasado mañana" → {{runtime.day_after_tomorrow}}
- Días de semana ("el viernes", "el próximo lunes") → próxima ocurrencia futura desde hoy.
- Número de día solo ("el 15", "para el 29") → ese día en el mes actual si aún no pasó, o en el mes siguiente si ya pasó.
- Si el usuario pide disponibilidad u horarios mencionando una fecha (incluso "¿y para mañana?", "¿tienen para el viernes?") → extraer la fecha correspondiente.

Reglas de horas (mapeo obligatorio):
- Convertir siempre a HH:MM formato 24h.
- "9am" → "09:00", "2pm" → "14:00", "mediodía" → "12:00", "a las 3" → "15:00" si es por la tarde según contexto.

Reglas de resolución contextual:
- Valor directo: el usuario proporciona el dato explícitamente ("quiero el Baby Spa Premium", "a las 10", "el viernes").
- Aceptación por referencia: el asistente mostró opciones y el usuario acepta ("sí", "ese", "la primera", "esa", "está bien") → resolver al valor EXACTO del catálogo usando el historial de conversación.
- Nombre parcial o variación: resolver al nombre exacto del catálogo ("el básico" → "Baby Spa Básico", "el premium" → "Baby Spa Premium").
- Si hay varios candidatos o la referencia es ambigua → marcar como ambigüedad tipo "referential", confidence < 0.6, no extraer.

Confianza esperada:
- Dato explícito e inequívoco: 0.95
- Referencia resuelta con certeza desde historial: 0.90
- Referencia ambigua o múltiples candidatos: 0.65–0.70
- Solo incluir campos con confidence >= 0.6';

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = JSON_MODIFY([DefinitionJson], '$.extractionInstructions', @ExtractionInstructions)
WHERE [FlowDefinitionId] = @FlowDefId;

-- ── Verify ────────────────────────────────────────────────────────────────────
SELECT
    fd.Name                                                          AS Flujo,
    JSON_VALUE(fd.DefinitionJson, '$.engineSettings.maxConversationHistoryMessages') AS MaxHistory,
    JSON_VALUE(fd.DefinitionJson, '$.engineSettings.extractionMaxTokens')            AS ExtractionMaxTokens,
    JSON_VALUE(fd.DefinitionJson, '$.engineSettings.extractionMinConfidence')        AS MinConfidence,
    LEFT(JSON_VALUE(fd.DefinitionJson, '$.extractionInstructions'), 80)              AS ExtractionInstructions_Preview
FROM [dbo].[FlowDefinitions] fd
WHERE fd.FlowDefinitionId = @FlowDefId;

COMMIT TRANSACTION;
PRINT 'Migration 010 completed. extractionInstructions injected into Mimo Bot flow.';
