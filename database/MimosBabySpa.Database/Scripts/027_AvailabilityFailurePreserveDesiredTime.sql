-- =============================================================================
-- Migration 027: Availability failure — preserve desired_time + alternatives UX
--
-- Root cause: check_availability output_mapping mapped desired_time ← confirmed_time.
-- When the slot was unavailable, confirmed_time was null and overwrote the user''s
-- extracted hour, so the bot appeared to ignore "a las 9".
--
-- Fixes (with C# ActionNodeHandler skipping null output_mapping values):
-- 1. Remove output_mapping.desired_time from check_availability (redundant; success path keeps desired_time as-is).
-- 2. show_alternatives: GenerateResponse (type 4) with instructions that reference {{variables.desired_time}}
--    and {{variables.available_time_slots}}; waitForUser true. User can change hour via extraction;
--    variable onChange (desired_time → check_availability) re-runs availability.
--
-- Idempotent: skips mapping removal / node update if already applied.
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

-- ── 1. check_availability: drop output_mapping.desired_time ─────────────────

DECLARE @CheckIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'check_availability'
);

IF @CheckIdx IS NOT NULL
BEGIN
    DECLARE @OutMap NVARCHAR(MAX) = JSON_QUERY(@Json, N'$.nodes[' + @CheckIdx + N'].config.output_mapping');
    IF @OutMap IS NOT NULL AND CHARINDEX(N'"desired_time"', @OutMap) > 0
    BEGIN
        SET @Json = JSON_MODIFY(@Json, N'$.nodes[' + @CheckIdx + N'].config.output_mapping.desired_time', NULL);
        PRINT '027: removed check_availability output_mapping.desired_time.';
    END
    ELSE
        PRINT '027: check_availability output_mapping.desired_time already absent — skip.';
END
ELSE
    PRINT '027: WARNING — check_availability node not found.';

-- ── 2. show_alternatives → GenerateResponse (4) + instructions ───────────────

DECLARE @ShowAltIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'show_alternatives'
);

DECLARE @ShowAltConfig NVARCHAR(MAX) = N'{"responseMode":"llm","waitForUser":true,"instructions":"El cliente pidió la hora {{variables.desired_time}} para el {{variables.desired_date}} y ese horario no está disponible. Horarios con cupo: {{variables.available_time_slots}}. Reconoce con empatía la hora que eligió, explica brevemente que no hay disponibilidad en ese momento y ofrece las alternativas para que elija una. Si en su mensaje indica otra hora, el sistema volverá a verificar la disponibilidad automáticamente."}';

IF @ShowAltIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json, N'$.nodes[' + @ShowAltIdx + N'].type', 4);
    SET @Json = JSON_MODIFY(
        @Json,
        N'$.nodes[' + @ShowAltIdx + N'].config',
        JSON_QUERY(@ShowAltConfig));
    PRINT '027: show_alternatives set to GenerateResponse (type 4) with desired_time-aware instructions.';
END
ELSE
    PRINT '027: WARNING — show_alternatives node not found.';

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = SYSUTCDATETIME()
WHERE [FlowDefinitionId] = @FlowDefId;

COMMIT TRANSACTION;
PRINT '027: completed.';
