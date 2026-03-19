-- =============================================================================
-- Migration 016: Restore and fix is_information_query intention + guards
--
-- State of DB after first (wrong) 016 execution:
--   - is_information_query was REMOVED from intentions array
--   - applyGuards on service/desired_date/desired_time were set to []
--
-- This script restores both with the correct, precise definition.
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @AgentId   UNIQUEIDENTIFIER;
DECLARE @FlowDefId UNIQUEIDENTIFIER;

SELECT TOP 1 @AgentId = a.AgentId FROM [dbo].[Agents] a WHERE a.Name = 'Mimo Bot';
SELECT TOP 1 @FlowDefId = fd.FlowDefinitionId
FROM [dbo].[FlowDefinitions] fd
WHERE fd.AgentId = @AgentId AND fd.IsActive = 1;

IF @AgentId IS NULL OR @FlowDefId IS NULL
BEGIN
    RAISERROR('Mimo Bot or active FlowDefinition not found.', 16, 1);
    ROLLBACK TRANSACTION; RETURN;
END

DECLARE @Json NVARCHAR(MAX);
SELECT @Json = [DefinitionJson] FROM [dbo].[FlowDefinitions] WHERE [FlowDefinitionId] = @FlowDefId;

-- =============================================================================
-- 1. Restore applyGuards on service/desired_date/desired_time
-- =============================================================================

DECLARE @varIdx INT;
DECLARE @guard NVARCHAR(MAX) = N'[{"type":"skipWhenIntention","value":"is_information_query"}]';

SELECT @varIdx = CAST([key] AS INT)
FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
WHERE JSON_VALUE(value, '$.key') = 'service';
IF @varIdx IS NOT NULL
    SET @Json = JSON_MODIFY(@Json, '$.variables[' + CAST(@varIdx AS NVARCHAR) + '].applyGuards', JSON_QUERY(@guard));

SELECT @varIdx = CAST([key] AS INT)
FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
WHERE JSON_VALUE(value, '$.key') = 'desired_date';
IF @varIdx IS NOT NULL
    SET @Json = JSON_MODIFY(@Json, '$.variables[' + CAST(@varIdx AS NVARCHAR) + '].applyGuards', JSON_QUERY(@guard));

SELECT @varIdx = CAST([key] AS INT)
FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
WHERE JSON_VALUE(value, '$.key') = 'desired_time';
IF @varIdx IS NOT NULL
    SET @Json = JSON_MODIFY(@Json, '$.variables[' + CAST(@varIdx AS NVARCHAR) + '].applyGuards', JSON_QUERY(@guard));

-- =============================================================================
-- 2. Insert is_information_query into intentions array (append)
--    The intention was deleted by a previous migration and must be re-added.
--    JSON is single-line to avoid SQL Server JSON parsing issues with newlines.
--
--    Key fix: precise linguistic criteria with detectionExamples AND
--    negativeExamples so the LLM classifier distinguishes "asking about X"
--    from "choosing/accepting X".
-- =============================================================================

DECLARE @IntentionJson NVARCHAR(MAX) = N'{"key":"is_information_query","description":"El usuario PREGUNTA por informacion usando marcadores linguisticos de solicitud: preguntas directas (cuanto cuesta, que incluye, cuales son, como funciona), solicitudes de informacion (cuentame, explicame, hablame de, dime sobre, quiero saber), o comparaciones (cual es la diferencia). NO aplica cuando el usuario esta eligiendo, aceptando o confirmando.","detectionExamples":["cuanto cuesta el baby spa","que incluye el plan marineritos","cuales son los planes disponibles","hablame sobre los planes","cuentame del baby spa","si sobre el baby spa","dime sobre la estimulacion","cual es la diferencia entre los planes"],"negativeExamples":["el marineritos esta bien","si ese plan","quiero el marineritos","ese plan me interesa","esta bien el marineritos","ese me gusta","me llevo ese plan"],"priority":10,"alwaysDetect":true,"behavior":{"action":"none"}}';

-- Check if it already exists (idempotent)
DECLARE @alreadyExists INT = 0;
SELECT @alreadyExists = COUNT(*)
FROM OPENJSON(JSON_QUERY(@Json, '$.intentions'))
WHERE JSON_VALUE(value, '$.key') = 'is_information_query';

IF @alreadyExists = 0
BEGIN
    -- Append to the intentions array
    DECLARE @currentIntentions NVARCHAR(MAX) = JSON_QUERY(@Json, '$.intentions');
    -- Remove closing bracket, append new element, re-close
    DECLARE @newIntentions NVARCHAR(MAX) =
        LEFT(@currentIntentions, LEN(@currentIntentions) - 1)
        + N',' + @IntentionJson + N']';

    SET @Json = JSON_MODIFY(@Json, '$.intentions', JSON_QUERY(@newIntentions));
END
ELSE
BEGIN
    -- Already exists: update description and examples in place
    DECLARE @intentIdx INT;
    SELECT @intentIdx = CAST([key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Json, '$.intentions'))
    WHERE JSON_VALUE(value, '$.key') = 'is_information_query';

    SET @Json = JSON_MODIFY(@Json, '$.intentions[' + CAST(@intentIdx AS NVARCHAR) + '].description',
        N'El usuario PREGUNTA por informacion usando marcadores linguisticos de solicitud: preguntas directas (cuanto cuesta, que incluye, cuales son, como funciona), solicitudes de informacion (cuentame, explicame, hablame de, dime sobre, quiero saber), o comparaciones (cual es la diferencia). NO aplica cuando el usuario esta eligiendo, aceptando o confirmando.');
END

-- =============================================================================
-- 3. Persist
-- =============================================================================

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = GETUTCDATE()
WHERE [FlowDefinitionId] = @FlowDefId;

-- =============================================================================
-- Verify
-- =============================================================================

-- Confirm intention is present
SELECT JSON_VALUE(i.value, '$.key') AS intention_key,
       LEFT(JSON_VALUE(i.value, '$.description'), 80) AS description_preview
FROM OPENJSON(JSON_QUERY(@Json, '$.intentions')) AS i
WHERE JSON_VALUE(i.value, '$.key') = 'is_information_query';

-- Confirm guards are restored
SELECT JSON_VALUE(v.value, '$.key') AS variable_key,
       JSON_QUERY(v.value, '$.applyGuards') AS apply_guards
FROM OPENJSON(JSON_QUERY(@Json, '$.variables')) AS v
WHERE JSON_VALUE(v.value, '$.key') IN ('service', 'desired_date', 'desired_time');

COMMIT TRANSACTION;
PRINT '=== Migration 016 completed. is_information_query restored with precise criteria. Guards restored. ===';
GO
