-- =============================================================================
-- Migration 026: resolve_pricing action + pricing variables + graph wiring
--
-- 1. Variables: service_price, addons_detail, total_price, total_price_invariant
--    (system-managed, populated by resolve_pricing; first three showInSummary)
-- 2. Node resolve_pricing between collect_identity and show_confirmation
-- 3. Edge e12: collect_identity → resolve_pricing; new e12b: resolve_pricing → show_confirmation
-- 4. service.onChange.resetVariables: clear pricing when service changes
-- 5. cancel_response.setVariables: null pricing + transactionalVariables list
-- 6. create_reservation.input_mapping: selected_add_ons
-- Idempotent: skips if node resolve_pricing already exists.
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

DECLARE @HasPricingNode NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'resolve_pricing'
);

IF @HasPricingNode IS NOT NULL
BEGIN
    PRINT '026: resolve_pricing node already present — skipping.';
    COMMIT TRANSACTION;
    RETURN;
END;

-- ── Variables (append) ──────────────────────────────────────────────────────

DECLARE @VarServicePrice NVARCHAR(MAX) = N'{"key":"service_price","label":"Precio del servicio","dataType":"String","required":false,"group":"transaction","displayOrder":10,"showInSummary":true,"isSystemManaged":true}';
DECLARE @VarAddonsDetail NVARCHAR(MAX) = N'{"key":"addons_detail","label":"Extras","dataType":"String","required":false,"group":"transaction","displayOrder":11,"showInSummary":true,"isSystemManaged":true}';
DECLARE @VarTotalPrice NVARCHAR(MAX) = N'{"key":"total_price","label":"Total","dataType":"String","required":false,"group":"transaction","displayOrder":12,"showInSummary":true,"isSystemManaged":true}';
DECLARE @VarTotalInv NVARCHAR(MAX) = N'{"key":"total_price_invariant","label":"Total (sistema)","dataType":"String","required":false,"group":"system","displayOrder":100,"showInSummary":false,"isSystemManaged":true}';

SET @Json = JSON_MODIFY(@Json, 'append $.variables', JSON_QUERY(@VarServicePrice));
SET @Json = JSON_MODIFY(@Json, 'append $.variables', JSON_QUERY(@VarAddonsDetail));
SET @Json = JSON_MODIFY(@Json, 'append $.variables', JSON_QUERY(@VarTotalPrice));
SET @Json = JSON_MODIFY(@Json, 'append $.variables', JSON_QUERY(@VarTotalInv));

-- anticipo_amount variable (populated by resolve_pricing when anticipoPercentage > 0)
IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.variables'))
    WHERE JSON_VALUE(value, N'$.key') = N'anticipo_amount'
)
    SET @Json = JSON_MODIFY(@Json, 'append $.variables',
        JSON_QUERY(N'{"key":"anticipo_amount","label":"Anticipo requerido","dataType":"String","required":false,"group":"system","displayOrder":101,"showInSummary":false,"isSystemManaged":true}'));

-- ── Node resolve_pricing ──────────────────────────────────────────────────────

DECLARE @ResolveNode NVARCHAR(MAX) = N'{"id":"resolve_pricing","type":2,"label":"Resolver precios","config":{"action_type":"resolve_pricing","input_mapping":{"item":"{{variables.service}}","selected_add_ons":"{{variables.selected_add_ons}}"},"pricing":{"anticipoPercentage":50},"output_mapping":{"service_price":"service_price","addons_detail":"addons_detail","total_price":"total_price","total_price_invariant":"total_price_invariant","anticipo_amount":"anticipo_amount"}}}';
SET @Json = JSON_MODIFY(@Json, 'append $.nodes', JSON_QUERY(@ResolveNode));

-- ── Edge e12 → resolve_pricing; append e12b ─────────────────────────────────

DECLARE @E12Idx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.edges'))
    WHERE JSON_VALUE(value, N'$.id') = N'e12'
);

IF @E12Idx IS NOT NULL
    SET @Json = JSON_MODIFY(@Json, '$.edges[' + @E12Idx + '].targetNodeId', N'resolve_pricing');

SET @Json = JSON_MODIFY(@Json, 'append $.edges', JSON_QUERY(N'{"id":"e12b","sourceNodeId":"resolve_pricing","targetNodeId":"generate_payment_link"}'));

-- ── service.onChange.resetVariables ───────────────────────────────────────────

DECLARE @SvcIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.variables'))
    WHERE JSON_VALUE(value, N'$.key') = N'service'
);

IF @SvcIdx IS NOT NULL
BEGIN
    DECLARE @ResetAfterService NVARCHAR(MAX) = N'["desired_time","available_time_slots","service_price","addons_detail","total_price","total_price_invariant"]';
    SET @Json = JSON_MODIFY(@Json, '$.variables[' + @SvcIdx + '].onChange.resetVariables', JSON_QUERY(@ResetAfterService));
END;

-- ── cancel_response.setVariables ─────────────────────────────────────────────

DECLARE @CancelIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'cancel_response'
);

IF @CancelIdx IS NOT NULL
BEGIN
    DECLARE @CancelSetVars NVARCHAR(MAX) = JSON_QUERY(@Json, '$.nodes[' + @CancelIdx + '].config.setVariables');
    IF @CancelSetVars IS NOT NULL AND @CancelSetVars NOT LIKE N'%total_price_invariant%'
    BEGIN
        SET @CancelSetVars = REPLACE(@CancelSetVars, N'}', N',"service_price":null,"addons_detail":null,"total_price":null,"total_price_invariant":null}');
        SET @Json = JSON_MODIFY(@Json, '$.nodes[' + @CancelIdx + '].config.setVariables', JSON_QUERY(@CancelSetVars));
    END;
END;

-- ── create_reservation.input_mapping.selected_add_ons ─────────────────────────

DECLARE @CreateIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'create_reservation'
);

IF @CreateIdx IS NOT NULL
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @CreateIdx + '].config.input_mapping.selected_add_ons',
        N'{{variables.selected_add_ons}}');

-- ── transactionalVariables ───────────────────────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.sessionConfig.transactionalVariables'))
    WHERE value = N'service_price'
)
    SET @Json = JSON_MODIFY(@Json, 'append $.sessionConfig.transactionalVariables', N'service_price');

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.sessionConfig.transactionalVariables'))
    WHERE value = N'addons_detail'
)
    SET @Json = JSON_MODIFY(@Json, 'append $.sessionConfig.transactionalVariables', N'addons_detail');

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.sessionConfig.transactionalVariables'))
    WHERE value = N'total_price'
)
    SET @Json = JSON_MODIFY(@Json, 'append $.sessionConfig.transactionalVariables', N'total_price');

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.sessionConfig.transactionalVariables'))
    WHERE value = N'total_price_invariant'
)
    SET @Json = JSON_MODIFY(@Json, 'append $.sessionConfig.transactionalVariables', N'total_price_invariant');

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.sessionConfig.transactionalVariables'))
    WHERE value = N'anticipo_amount'
)
    SET @Json = JSON_MODIFY(@Json, 'append $.sessionConfig.transactionalVariables', N'anticipo_amount');

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = SYSUTCDATETIME()
WHERE [FlowDefinitionId] = @FlowDefId;

COMMIT TRANSACTION;
PRINT '026_ResolvePricingAndFlowTotals completed.';
GO
