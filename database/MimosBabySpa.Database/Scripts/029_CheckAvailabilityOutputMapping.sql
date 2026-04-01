-- =============================================================================
-- Migration 029: Add desired_time → confirmed_time to check_availability output_mapping
-- =============================================================================
-- Root cause: when check_availability returns failure (requested time is unavailable),
-- confirmed_time is null. The engine now clears state variables mapped to null action
-- outputs, so desired_time gets removed. This allows the downstream CollectFields node
-- (show_alternatives) to ask for a new time instead of looping infinitely with the
-- rejected value.
-- =============================================================================

DECLARE @FlowDefId UNIQUEIDENTIFIER = (
    SELECT TOP 1 FlowDefinitionId
    FROM FlowDefinitions
    WHERE IsActive = 1
    ORDER BY CreatedAt DESC
);

IF @FlowDefId IS NULL
BEGIN
    PRINT '029: No active flow definition found — skipping.';
    RETURN;
END;

DECLARE @Json NVARCHAR(MAX) = (
    SELECT DefinitionJson FROM FlowDefinitions WHERE FlowDefinitionId = @FlowDefId
);

-- Find the check_availability node index
DECLARE @NodeIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'check_availability'
);

IF @NodeIdx IS NULL
BEGIN
    PRINT '029: check_availability node not found — skipping.';
    RETURN;
END;

-- Only add if not already present
DECLARE @ExistingMapping NVARCHAR(MAX) = JSON_QUERY(@Json, '$.nodes[' + @NodeIdx + '].config.output_mapping');

IF @ExistingMapping LIKE N'%desired_time%'
BEGIN
    PRINT '029: desired_time mapping already exists — skipping.';
    RETURN;
END;

SET @Json = JSON_MODIFY(
    @Json,
    '$.nodes[' + @NodeIdx + '].config.output_mapping.desired_time',
    'confirmed_time'
);

UPDATE FlowDefinitions
SET DefinitionJson = @Json,
    UpdatedAt = GETUTCDATE()
WHERE FlowDefinitionId = @FlowDefId;

-- Verify
DECLARE @Verify NVARCHAR(MAX) = JSON_VALUE(@Json, '$.nodes[' + @NodeIdx + '].config.output_mapping.desired_time');
PRINT '029: check_availability output_mapping.desired_time = ' + ISNULL(@Verify, 'NULL');
