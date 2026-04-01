-- =============================================================================
-- Migration 030: Generic pricing — eliminate redundant variables, generic hint
--
-- Root cause:
--   1. ReservationPricingResolver had hardcoded service/add-on distinction.
--      Now it's generic: resolves any item against the catalog and writes
--      "Name — $Price" back to the source variable via output_mapping.
--
--   2. extractionHint for selected_add_ons fabricated names ("Cumplemes decoracion
--      sencilla") that didn't exist in the Services table. Now the hint is generic:
--      extract whatever the user says, ServiceNameResolver handles matching.
--
--   3. service_price and addons_detail were redundant display-only variables.
--      Eliminated: the price now lives inside service and selected_add_ons
--      themselves (e.g. "Plan Marineritos — $100,000").
--
-- Changes:
--   1. Remove variables: service_price, addons_detail
--   2. resolve_pricing.output_mapping: write formatted items back to source vars
--   3. selected_add_ons.extractionHint: extract canonical name from conversation
--   4. offer_addons instructions: use real catalog names
--   5. service.onChange.resetVariables: remove service_price, addons_detail
--   6. cancel_response.setVariables: remove service_price, addons_detail
--   7. transactionalVariables: remove service_price, addons_detail
--   8. extractionInstructions: canonical name rule (use name as bot presented it)
--
-- Idempotent: safe to re-run.
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

-- ── 1. Remove variable: service_price ────────────────────────────────────────

DECLARE @ServicePriceIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'service_price'
);

IF @ServicePriceIdx IS NOT NULL
BEGIN
    DECLARE @VarsWithoutSP NVARCHAR(MAX) = N'[';
    DECLARE @VarIdx1 INT = 0;
    DECLARE @VarCount1 INT = (SELECT COUNT(*) FROM OPENJSON(JSON_QUERY(@Json, '$.variables')));

    SELECT @VarsWithoutSP = @VarsWithoutSP +
        CASE WHEN @VarIdx1 > 0 THEN ',' ELSE '' END + value,
        @VarIdx1 = @VarIdx1 + 1
    FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') <> 'service_price';

    SET @VarsWithoutSP = @VarsWithoutSP + N']';
    SET @Json = JSON_MODIFY(@Json, '$.variables', JSON_QUERY(@VarsWithoutSP));
    PRINT '030: variable service_price removed.';
END
ELSE
    PRINT '030: variable service_price not found — already removed.';

-- ── 2. Remove variable: addons_detail ────────────────────────────────────────

DECLARE @AddonsDetailIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'addons_detail'
);

IF @AddonsDetailIdx IS NOT NULL
BEGIN
    DECLARE @VarsWithoutAD NVARCHAR(MAX) = N'[';
    DECLARE @VarIdx2 INT = 0;

    SELECT @VarsWithoutAD = @VarsWithoutAD +
        CASE WHEN @VarIdx2 > 0 THEN ',' ELSE '' END + value,
        @VarIdx2 = @VarIdx2 + 1
    FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') <> 'addons_detail';

    SET @VarsWithoutAD = @VarsWithoutAD + N']';
    SET @Json = JSON_MODIFY(@Json, '$.variables', JSON_QUERY(@VarsWithoutAD));
    PRINT '030: variable addons_detail removed.';
END
ELSE
    PRINT '030: variable addons_detail not found — already removed.';

-- ── 3. resolve_pricing.output_mapping: write formatted back to source vars ───

DECLARE @ResolvePricingIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'resolve_pricing'
);

IF @ResolvePricingIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @ResolvePricingIdx + '].config.output_mapping',
        JSON_QUERY(N'{"service":"item","selected_add_ons":"selected_add_ons","total_price":"total_price","total_price_invariant":"total_price_invariant"}')
    );
    PRINT '030: resolve_pricing output_mapping updated.';
END
ELSE
    PRINT '030: WARNING — resolve_pricing node not found.';

-- ── 4. selected_add_ons.extractionHint: generic ─────────────────────────────

DECLARE @AddOnsVarIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'selected_add_ons'
);

IF @AddOnsVarIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(
        @Json,
        '$.variables[' + @AddOnsVarIdx + '].extractionHint',
        N'Nombre completo del complemento como aparece en el mensaje del asistente, no la versión abreviada del usuario. Si rechaza o dice ninguno o no quiere: ninguno.'
    );
    PRINT '030: selected_add_ons extractionHint updated.';
END
ELSE
    PRINT '030: WARNING — variable selected_add_ons not found.';

-- ── 5. offer_addons instructions: real catalog names ─────────────────────────

DECLARE @OfferAddonsIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'offer_addons'
);

IF @OfferAddonsIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @OfferAddonsIdx + '].config',
        JSON_QUERY(N'{
          "fields": ["selected_add_ons"],
          "instructions": "El cliente eligió el servicio indicado en DATOS RECOPILADOS. De forma natural y sin presionar, menciona los complementos opcionales de decoración disponibles: Decoración Sencilla ($35.000) y Decoración Bouquet Personalizado ($55.000). Pregunta si desea agregar alguno. Si hace preguntas sobre los complementos, respóndelas con detalle y vuelve a preguntar su decisión. Solo avanza cuando el cliente indique su elección o rechace explícitamente."
        }')
    );
    PRINT '030: offer_addons instructions updated.';
END
ELSE
    PRINT '030: WARNING — offer_addons node not found.';

-- ── 6. service.onChange.resetVariables: remove service_price, addons_detail ──

DECLARE @SvcIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'service'
);

IF @SvcIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(
        @Json,
        '$.variables[' + @SvcIdx + '].onChange.resetVariables',
        JSON_QUERY(N'["desired_time","available_time_slots","total_price","total_price_invariant"]')
    );
    PRINT '030: service.onChange.resetVariables cleaned.';
END;

-- ── 7. cancel_response.setVariables: remove service_price, addons_detail ─────

DECLARE @CancelIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'cancel_response'
);

IF @CancelIdx IS NOT NULL
BEGIN
    DECLARE @CancelSetVars NVARCHAR(MAX) = JSON_QUERY(@Json, '$.nodes[' + @CancelIdx + '].config.setVariables');
    IF @CancelSetVars IS NOT NULL
    BEGIN
        -- Rebuild without service_price and addons_detail
        DECLARE @NewCancelVars NVARCHAR(MAX) = N'{';
        DECLARE @First BIT = 1;

        SELECT
            @NewCancelVars = @NewCancelVars +
                CASE WHEN @First = 1 THEN '' ELSE ',' END +
                '"' + [key] + '":' +
                CASE WHEN value = 'null' OR value IS NULL THEN 'null' ELSE '"' + value + '"' END,
            @First = 0
        FROM OPENJSON(@CancelSetVars)
        WHERE [key] NOT IN ('service_price', 'addons_detail');

        SET @NewCancelVars = @NewCancelVars + N'}';
        SET @Json = JSON_MODIFY(
            @Json,
            '$.nodes[' + @CancelIdx + '].config.setVariables',
            JSON_QUERY(@NewCancelVars)
        );
        PRINT '030: cancel_response.setVariables cleaned.';
    END;
END;

-- ── 8. transactionalVariables: remove service_price, addons_detail ───────────

DECLARE @TransVars NVARCHAR(MAX) = JSON_QUERY(@Json, '$.sessionConfig.transactionalVariables');
IF @TransVars IS NOT NULL
BEGIN
    DECLARE @NewTransVars NVARCHAR(MAX) = N'[';
    DECLARE @TFirst BIT = 1;

    SELECT
        @NewTransVars = @NewTransVars +
            CASE WHEN @TFirst = 1 THEN '' ELSE ',' END +
            '"' + value + '"',
        @TFirst = 0
    FROM OPENJSON(@TransVars)
    WHERE value NOT IN ('service_price', 'addons_detail');

    SET @NewTransVars = @NewTransVars + N']';
    SET @Json = JSON_MODIFY(
        @Json,
        '$.sessionConfig.transactionalVariables',
        JSON_QUERY(@NewTransVars)
    );
    PRINT '030: transactionalVariables cleaned.';
END;

-- ── 9. extractionInstructions: canonical name rule ───────────────────────────
--    When the bot presents specific options/names and the user accepts with an
--    abbreviation, extract the FULL canonical name from the bot's message.
--    This is the root fix: the LLM has the context to produce the right name.

DECLARE @MarkerCanonical NVARCHAR(100) = N'Regla de nombre canónico';
DECLARE @CurrentInstr NVARCHAR(MAX) = JSON_VALUE(@Json, '$.extractionInstructions');

IF @CurrentInstr IS NOT NULL AND CHARINDEX(@MarkerCanonical, @CurrentInstr) = 0
BEGIN
    DECLARE @CanonicalRule NVARCHAR(MAX) =
        CHAR(10) + CHAR(10)
        + N'Regla de nombre canónico:' + CHAR(10)
        + N'- Cuando el asistente presenta opciones con nombres específicos (servicios, planes,'  + CHAR(10)
        + N'  complementos, horarios) y el usuario acepta o elige usando una versión abreviada,'  + CHAR(10)
        + N'  extraer el nombre COMPLETO tal como apareció en el mensaje del asistente.'          + CHAR(10)
        + N'  Ejemplo: asistente dijo "Decoración Sencilla ($35.000)" y usuario dice'             + CHAR(10)
        + N'  "sí la sencilla" → extraer: Decoración Sencilla (no "sencilla", no "la sencilla").' + CHAR(10)
        + N'- Esta regla aplica a cualquier campo donde el valor sea una opción presentada'       + CHAR(10)
        + N'  previamente por el asistente.';

    SET @Json = JSON_MODIFY(@Json, '$.extractionInstructions', @CurrentInstr + @CanonicalRule);
    PRINT '030: extractionInstructions — canonical name rule appended.';
END
ELSE IF @CurrentInstr IS NOT NULL
    PRINT '030: extractionInstructions — canonical name rule already present.';
ELSE
    PRINT '030: WARNING — extractionInstructions is NULL, rule not applied.';

-- ── Apply ────────────────────────────────────────────────────────────────────

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = SYSUTCDATETIME()
WHERE [FlowDefinitionId] = @FlowDefId;

-- ── Verify ───────────────────────────────────────────────────────────────────

DECLARE @VerifyVarCount INT = (
    SELECT COUNT(*) FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') IN ('service_price', 'addons_detail')
);
PRINT '030: leftover service_price/addons_detail vars = ' + CAST(@VerifyVarCount AS NVARCHAR(5)) + ' (expected: 0)';

DECLARE @VerifyOM NVARCHAR(MAX) = JSON_QUERY(@Json, '$.nodes[' + ISNULL(@ResolvePricingIdx,'0') + '].config.output_mapping');
PRINT '030: resolve_pricing output_mapping = ' + ISNULL(@VerifyOM, 'NULL');

COMMIT TRANSACTION;
PRINT '030_GenericPricingAndAddOnsFix completed.';
GO
