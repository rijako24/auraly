-- =============================================================================
-- Migration 025: Clear confirmation_summary_presented on detect_confirmation
--
-- Root cause: show_confirmation sets confirmation_summary_presented=true so
-- user_confirmed_booking is gated in extraction. After detect_confirmation
-- routes to wants_changes (or any branch), the flag must turn off immediately —
-- otherwise subsequent turns (offer_addons, collect_service) still see the
-- intention active and short affirmatives mis-fire as booking confirmation.
--
-- Design: stage flags are consumed by the node that processes the user's reply
-- to that stage (set at presenter, clear at consumer). No orchestrator patch.
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @FlowDefId UNIQUEIDENTIFIER = (
    SELECT TOP 1 [FlowDefinitionId] FROM [dbo].[FlowDefinitions] ORDER BY [CreatedAt] DESC
);

DECLARE @Json NVARCHAR(MAX) = (
    SELECT [DefinitionJson] FROM [dbo].[FlowDefinitions] WHERE [FlowDefinitionId] = @FlowDefId
);

IF @Json IS NULL
BEGIN
    RAISERROR('FlowDefinition not found', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @DetectIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'detect_confirmation'
);

IF @DetectIdx IS NULL
BEGIN
    RAISERROR('Node detect_confirmation not found', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @ExistingSetFlags NVARCHAR(MAX) = JSON_QUERY(@Json, '$.nodes[' + @DetectIdx + '].config.setFlags');

IF @ExistingSetFlags IS NULL
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @DetectIdx + '].config.setFlags',
        JSON_QUERY(N'{"confirmation_summary_presented":false}')
    );
ELSE
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @DetectIdx + '].config.setFlags.confirmation_summary_presented',
        CONVERT(BIT, 0)
    );

UPDATE [dbo].[FlowDefinitions]
SET    [DefinitionJson] = @Json,
       [UpdatedAt]      = GETUTCDATE()
WHERE  [FlowDefinitionId] = @FlowDefId;

PRINT '025: detect_confirmation now clears confirmation_summary_presented after routing.';
PRINT '  setFlags merge = ' + CASE WHEN @ExistingSetFlags IS NULL THEN 'new object' ELSE 'property merge' END;

COMMIT TRANSACTION;
GO
