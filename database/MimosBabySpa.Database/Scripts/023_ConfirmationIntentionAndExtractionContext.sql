-- =============================================================================
-- Migration 023: Confirmation intention + generic contextual intention rules
--
-- Root cause: short affirmations ("si" without accent, "ok", "dale") were not
-- reliably mapped to user_confirmed_booking; IntentionRouter then fell through
-- to wants_changes.
--
-- Changes:
--   1. Refine user_confirmed_booking (description + examples). Remove wording
--      that raises the bar ("explícitamente", meta-instruction about when to
--      evaluate — stageCondition already gates this in code).
--   2. Append generic extractionInstructions block: any flow can reuse the same
--      rule for short yes/no style messages vs. last assistant turn (no hardcoded
--      intention keys in the rule text).
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @FlowDefId UNIQUEIDENTIFIER = (
    SELECT TOP 1 [FlowDefinitionId] FROM [dbo].[FlowDefinitions] ORDER BY [CreatedAt] DESC
);

DECLARE @DefinitionJson NVARCHAR(MAX) = (
    SELECT [DefinitionJson] FROM [dbo].[FlowDefinitions] WHERE [FlowDefinitionId] = @FlowDefId
);

IF @DefinitionJson IS NULL
BEGIN
    RAISERROR('FlowDefinition not found', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Update intentionSchema entry: user_confirmed_booking
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @ConfirmIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@DefinitionJson, '$.intentionSchema'))
    WHERE JSON_VALUE(value, '$.key') = 'user_confirmed_booking'
);

IF @ConfirmIdx IS NULL
BEGIN
    RAISERROR('intentionSchema entry user_confirmed_booking not found', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @NewDescription NVARCHAR(500) = N'El usuario acepta o confirma los datos mostrados en el resumen. Cualquier respuesta afirmativa, aprobatoria o de acuerdo cuenta como confirmación de que los datos son correctos y quiere proceder.';

SET @DefinitionJson = JSON_MODIFY(
    @DefinitionJson,
    '$.intentionSchema[' + @ConfirmIdx + '].description',
    @NewDescription
);

SET @DefinitionJson = JSON_MODIFY(
    @DefinitionJson,
    '$.intentionSchema[' + @ConfirmIdx + '].examples',
    JSON_QUERY(N'["sí","si","sip","confirmo","confirmado","está bien","así está bien","perfecto","dale","ok","claro","listo","va","correcto","todo bien","perfecto así"]')
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Append generic extractionInstructions (idempotent)
--    Use OPENJSON WITH (NVARCHAR(MAX)) so long instructions are not truncated
--    (JSON_VALUE caps at 4000 chars).
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @Marker NVARCHAR(120) = N'Detección de intenciones con contexto conversacional';

DECLARE @CurrentInstr NVARCHAR(MAX);
SELECT @CurrentInstr = extractionInstructions
FROM OPENJSON(@DefinitionJson)
WITH (extractionInstructions NVARCHAR(MAX) '$.extractionInstructions');

DECLARE @InstrAppended BIT = 0;

IF @CurrentInstr IS NOT NULL AND CHARINDEX(@Marker, @CurrentInstr) = 0
BEGIN
    DECLARE @ContextBlock NVARCHAR(MAX) =
        CHAR(10) + CHAR(10)
        + N'Detección de intenciones con contexto conversacional:' + CHAR(10)
        + N'- Las respuestas cortas afirmativas (si, sí, ok, dale, claro, perfecto, listo, va, bien, correcto, sip, etc.) o negativas (no, nop, para nada, etc.) por sí solas no contienen datos extraíbles, pero SÍ pueden activar intenciones.' + CHAR(10)
        + N'- Usa el último mensaje del asistente como contexto para determinar qué intención aplica. Si el asistente pidió confirmación, aprobación o aceptación y el usuario responde afirmativamente, la intención de la lista cuya descripción encaje con esa petición debe marcarse como true.' + CHAR(10)
        + N'- La conversación previa tiene prioridad sobre la interpretación literal del mensaje cuando este es una expresión breve de acuerdo o desacuerdo.';

    SET @DefinitionJson = JSON_MODIFY(
        @DefinitionJson,
        '$.extractionInstructions',
        @CurrentInstr + @ContextBlock
    );
    SET @InstrAppended = 1;
END;

-- ─────────────────────────────────────────────────────────────────────────────
-- Apply
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @DefinitionJson,
    [UpdatedAt]      = GETUTCDATE()
WHERE [FlowDefinitionId] = @FlowDefId;

PRINT '023: user_confirmed_booking updated at intentionSchema index ' + ISNULL(@ConfirmIdx, 'NULL');
IF @InstrAppended = 1
    PRINT '023: generic contextual intention rules appended to extractionInstructions.';
ELSE IF @CurrentInstr IS NOT NULL AND CHARINDEX(@Marker, @CurrentInstr) > 0
    PRINT '023: extractionInstructions context block already present — skipped append.';
ELSE
    PRINT '023: extractionInstructions was NULL — context block not appended.';

COMMIT TRANSACTION;
GO
