-- =============================================================================
-- Migration 028: Anticipo 50% activo, auto-escalación por turnos degradados,
--                plantilla success_response alineada con pricing (collected_data),
--                excepciones de acción → puerto failure (código en ActionNodeHandler).
--
-- 1. engineSettings.degradedTurnsBeforeEscalation = 2
-- 2. generate_payment_link.config.payment: requiresAnticipo true, anticipo 50%, COP, 24h
-- 3. success_response: template con {{collected_data}} + branding Mimo's
--
-- Idempotent: safe to re-run (overwrites same targets).
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @AgentId   UNIQUEIDENTIFIER;
DECLARE @FlowDefId UNIQUEIDENTIFIER;

SELECT TOP 1 @AgentId = a.AgentId FROM [dbo].[Agents] a WHERE a.Name = N'Mimo Bot';
SELECT TOP 1 @FlowDefId = fd.FlowDefinitionId
FROM [dbo].[FlowDefinitions] fd
WHERE fd.AgentId = @AgentId AND fd.IsActive = 1;

IF @AgentId IS NULL OR @FlowDefId IS NULL
BEGIN
    RAISERROR('Mimo Bot or active FlowDefinition not found.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @Json NVARCHAR(MAX) = (
    SELECT [DefinitionJson] FROM [dbo].[FlowDefinitions] WHERE [FlowDefinitionId] = @FlowDefId
);

IF @Json IS NULL
BEGIN
    RAISERROR('FlowDefinition JSON is NULL.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

-- ── 1. Degraded turns before escalation ─────────────────────────────────────

SET @Json = JSON_MODIFY(@Json, N'$.engineSettings.degradedTurnsBeforeEscalation', CAST(2 AS INT));
PRINT '028: engineSettings.degradedTurnsBeforeEscalation = 2';

-- ── 2. generate_payment_link — payment block ───────────────────────────────

DECLARE @GenPayIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'generate_payment_link'
);

IF @GenPayIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(
        @Json,
        N'$.nodes[' + @GenPayIdx + N'].config.payment',
        JSON_QUERY(N'{"requiresAnticipo":true,"anticipoPercentage":50,"currency":"COP","expirationMinutes":1440}'));
    PRINT '028: generate_payment_link.payment set (anticipo 50%).';
END
ELSE
    PRINT '028: WARNING — generate_payment_link node not found.';

-- ── 3. success_response — instructions ─────────────────────────────────────

DECLARE @SuccessIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'success_response'
);

DECLARE @SuccessInstructions NVARCHAR(MAX) = N'🎉 *¡Reserva confirmada!*

📋 *Número de reserva:* #{{variables.reservation_id}}

{{collected_data}}

¡Te esperamos con mucho cariño en Mimo''s Baby Spa! 💕
Si necesitas cambiar tu cita o tienes alguna pregunta, escríbenos con gusto.';

IF @SuccessIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(
        @Json,
        N'$.nodes[' + @SuccessIdx + N'].config.instructions',
        @SuccessInstructions);
    PRINT '028: success_response instructions updated (collected_data).';
END
ELSE
    PRINT '028: WARNING — success_response node not found.';

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = SYSUTCDATETIME()
WHERE [FlowDefinitionId] = @FlowDefId;

COMMIT TRANSACTION;
PRINT '028_AnticipoDegradedTurnsSuccessTemplate completed.';
GO
