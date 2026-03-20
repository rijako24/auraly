-- =============================================================================
-- Migration 030: LocalIntentions + Pricing Refactor
--
-- Root cause fixes applied after FlowExtractionService was updated to read
-- localIntentions from the current node (no stageCondition workaround needed).
--
-- Changes:
--   1. resolve_pricing: add "pricing" config block {anticipoPercentage:50}
--      and "anticipo_amount" to output_mapping.
--      ResolvePricingAction now formats and outputs anticipo_amount directly.
--   2. generate_payment_link.output_mapping: remove "anticipo_amount" and
--      "flag:payment_link_generated". Action outputs raw data only
--      (link_url, reference_id, anticipo_amount_cents).
--   3. IntentionSchema: remove "user_says_paid" and "user_wants_new_link" from
--      global schema — they live as localIntentions on wait_payment node and
--      are now detected by the fixed FlowExtractionService.
--   4. cancel_response.setFlags: remove "payment_link_generated" (flag gone).
--   5. Variables: ensure anticipo_amount is defined and in transactionalVariables.
--
-- Idempotent: skips if resolve_pricing already has "pricing" block.
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

-- ── Idempotency check ─────────────────────────────────────────────────────────

DECLARE @ResolvePricingIdx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'resolve_pricing'
);

IF @ResolvePricingIdx IS NOT NULL
BEGIN
    DECLARE @ExistingPricingBlock NVARCHAR(MAX) = JSON_QUERY(
        @Json, N'$.nodes[' + @ResolvePricingIdx + N'].config.pricing');
    IF @ExistingPricingBlock IS NOT NULL
    BEGIN
        PRINT '030: resolve_pricing already has pricing block — skipping.';
        COMMIT TRANSACTION;
        RETURN;
    END;
END;

-- ── 1. resolve_pricing: add pricing block + anticipo_amount to output_mapping ─

IF @ResolvePricingIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @ResolvePricingIdx + N'].config.pricing',
        JSON_QUERY(N'{"anticipoPercentage":50}'));

    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @ResolvePricingIdx + N'].config.output_mapping.anticipo_amount',
        N'anticipo_amount');

    PRINT '030: resolve_pricing — pricing block and anticipo_amount output added.';
END
ELSE
    PRINT '030: WARNING — resolve_pricing node not found (run migration 026 first).';

-- ── 2. generate_payment_link: replace output_mapping with raw-data-only version ─

DECLARE @GenPayIdx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'generate_payment_link'
);

IF @GenPayIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @GenPayIdx + N'].config.output_mapping',
        JSON_QUERY(N'{"payment_link_url":"link_url","payment_reference_id":"reference_id","anticipo_amount_cents":"anticipo_amount_cents"}'));

    PRINT '030: generate_payment_link.output_mapping replaced (raw data only).';
END
ELSE
    PRINT '030: WARNING — generate_payment_link node not found.';

-- ── 3. IntentionSchema: remove user_says_paid and user_wants_new_link ─────────
--    These intentions now live exclusively as localIntentions on wait_payment.
--    FlowExtractionService reads them from the node config — no global entry needed.

DECLARE @IntentionsPath NVARCHAR(20);

-- Detect the key used for the intentions array (may be 'intentions' or 'intentionSchema')
IF JSON_QUERY(@Json, N'$.intentions') IS NOT NULL
    SET @IntentionsPath = N'$.intentions';
ELSE IF JSON_QUERY(@Json, N'$.intentionSchema') IS NOT NULL
    SET @IntentionsPath = N'$.intentionSchema';

IF @IntentionsPath IS NOT NULL
BEGIN
    DECLARE @FilteredIntentions NVARCHAR(MAX);
    SELECT @FilteredIntentions = CONCAT(N'[', STRING_AGG(CAST(value AS NVARCHAR(MAX)), N','), N']')
    FROM OPENJSON(JSON_QUERY(@Json, @IntentionsPath))
    WHERE JSON_VALUE(value, N'$.key') NOT IN (N'user_says_paid', N'user_wants_new_link');

    SET @Json = JSON_MODIFY(@Json, @IntentionsPath, JSON_QUERY(@FilteredIntentions));
    PRINT '030: user_says_paid and user_wants_new_link removed from global IntentionSchema.';
END
ELSE
    PRINT '030: WARNING — could not find intentions array in flow definition.';

-- ── 4. cancel_response.setFlags: remove payment_link_generated ───────────────

DECLARE @CancelIdx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'cancel_response'
);

IF @CancelIdx IS NOT NULL
BEGIN
    DECLARE @CancelFlags NVARCHAR(MAX) = JSON_QUERY(
        @Json, N'$.nodes[' + @CancelIdx + N'].config.setFlags');

    IF @CancelFlags IS NOT NULL AND @CancelFlags LIKE N'%payment_link_generated%'
    BEGIN
        -- Rebuild setFlags without payment_link_generated
        SET @CancelFlags = JSON_MODIFY(@CancelFlags, N'$.payment_link_generated', NULL);
        SET @Json = JSON_MODIFY(@Json,
            N'$.nodes[' + @CancelIdx + N'].config.setFlags',
            JSON_QUERY(@CancelFlags));
        PRINT '030: cancel_response.setFlags — payment_link_generated removed.';
    END
    ELSE
        PRINT '030: cancel_response.setFlags — payment_link_generated already absent.';

    -- Ensure anticipo_amount is cleared on cancel (JSON literal null — NOT JSON_QUERY('null'),
    -- which fails: JSON_QUERY only accepts object/array fragments, not scalar null.)
    DECLARE @CancelSetVars NVARCHAR(MAX) = JSON_QUERY(
        @Json, N'$.nodes[' + @CancelIdx + N'].config.setVariables');
    IF @CancelSetVars IS NOT NULL
    BEGIN
        SET @CancelSetVars = JSON_MODIFY(@CancelSetVars, N'$.anticipo_amount', NULL);
        IF @CancelSetVars = N'{}'
            SET @CancelSetVars = N'{"anticipo_amount":null}';
        ELSE
            SET @CancelSetVars = STUFF(
                @CancelSetVars,
                LEN(@CancelSetVars),
                0,
                N',"anticipo_amount":null');
        SET @Json = JSON_MODIFY(
            @Json,
            N'$.nodes[' + @CancelIdx + N'].config.setVariables',
            JSON_QUERY(@CancelSetVars));
        PRINT '030: cancel_response.setVariables — anticipo_amount=null ensured.';
    END;
END
ELSE
    PRINT '030: WARNING — cancel_response node not found.';

-- ── 5. Variables: ensure anticipo_amount is defined ───────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.variables'))
    WHERE JSON_VALUE(value, N'$.key') = N'anticipo_amount'
)
BEGIN
    SET @Json = JSON_MODIFY(@Json, N'append $.variables',
        JSON_QUERY(N'{"key":"anticipo_amount","label":"Anticipo requerido","dataType":"String","required":false,"group":"system","displayOrder":101,"showInSummary":false,"isSystemManaged":true}'));
    PRINT '030: anticipo_amount variable added.';
END
ELSE
    PRINT '030: anticipo_amount variable already exists — skipping.';

-- ── 5b. transactionalVariables ────────────────────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.sessionConfig.transactionalVariables'))
    WHERE value = N'anticipo_amount'
)
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        N'append $.sessionConfig.transactionalVariables', N'anticipo_amount');
    PRINT '030: anticipo_amount added to transactionalVariables.';
END
ELSE
    PRINT '030: anticipo_amount already in transactionalVariables — skipping.';

-- ── Save ──────────────────────────────────────────────────────────────────────

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = SYSUTCDATETIME()
WHERE [FlowDefinitionId] = @FlowDefId;

COMMIT TRANSACTION;
PRINT '030_LocalIntentionsAndPricingRefactor completed.';
GO
