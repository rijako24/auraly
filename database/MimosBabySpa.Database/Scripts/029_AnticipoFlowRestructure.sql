-- =============================================================================
-- Migration 029: Anticipo flow restructure
--
-- Problema anterior:
--   generate_payment_link estaba DESPUÉS de show_confirmation, por lo que
--   el link de pago no existía cuando se mostraba el resumen al usuario.
--   Además, el camino con anticipo pedía confirmación innecesariamente.
--
-- Cambios:
--   1. generate_payment_link: agregar executeWhen (skip en reagendamientos),
--      payment block, y anticipo_amount en output_mapping.
--   2. wait_payment.waitingMessage: template completo con resumen + anticipo + link.
--   3. Edges: collect_identity/resolve_pricing → generate_payment_link (era show_confirmation).
--             generate_payment_link (not_required, skipped) → show_confirmation.
--             reschedule_reservation (skipped) → create_reservation (era generate_payment_link).
--             Nuevo edge: generate_payment_link (skipped) → show_confirmation.
--   4. IntentionSchema: agregar user_says_paid y user_wants_new_link con behavior:none
--      y stageCondition para que WaitForEventNodeHandler.localIntentions los detecte.
--   5. cancel_response.setVariables: agregar anticipo_amount: null.
--
-- Nuevo grafo:
--   CON ANTICIPO:  collect_identity → resolve_pricing → generate_payment_link (success) → wait_payment
--   SIN ANTICIPO:  collect_identity → resolve_pricing → generate_payment_link (not_required) → show_confirmation
--   REAGENDAMIENTO: resolve_pricing → generate_payment_link (skipped) → show_confirmation
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

-- ── Helpers: índices de nodos ────────────────────────────────────────────────

DECLARE @GenPayIdx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'generate_payment_link'
);

DECLARE @WaitPayIdx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'wait_payment'
);

DECLARE @CancelIdx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.nodes'))
    WHERE JSON_VALUE(value, N'$.id') = N'cancel_response'
);

-- ── 1. generate_payment_link: executeWhen + payment block + anticipo_amount ──

IF @GenPayIdx IS NOT NULL
BEGIN
    -- executeWhen: skip when is_rescheduling (reagendamientos no necesitan link nuevo)
    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @GenPayIdx + N'].config.executeWhen',
        JSON_QUERY(N'{"type":"FlagIsFalse","parameters":{"flag":"is_rescheduling"}}'));

    -- payment block
    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @GenPayIdx + N'].config.payment',
        JSON_QUERY(N'{"requiresAnticipo":true,"anticipoPercentage":50,"currency":"COP","expirationMinutes":1440}'));

    -- output_mapping: agregar anticipo_amount y flag:payment_link_generated
    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @GenPayIdx + N'].config.output_mapping.anticipo_amount',
        N'anticipo_amount');
    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @GenPayIdx + N'].config.output_mapping."flag:payment_link_generated"',
        N'payment_link_generated');

    PRINT '029: generate_payment_link updated (executeWhen + payment + anticipo_amount + flag).';
END
ELSE
    PRINT '029: WARNING — generate_payment_link node not found.';

-- ── 2. wait_payment: waitingMessage con resumen completo ─────────────────────

IF @WaitPayIdx IS NOT NULL
BEGIN
    DECLARE @WaitMsg NVARCHAR(MAX) =
        N'📋 *Resumen de tu reserva:*' + CHAR(10) + CHAR(10)
        + N'{{collected_data}}' + CHAR(10) + CHAR(10)
        + N'💰 Para confirmar tu reserva se requiere un anticipo de {{variables.anticipo_amount}}:' + CHAR(10) + CHAR(10)
        + N'💳 {{variables.payment_link_url}}' + CHAR(10) + CHAR(10)
        + N'Una vez realizado el pago, tu reserva quedará confirmada automáticamente 🎉';

    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @WaitPayIdx + N'].config.waitingMessage',
        @WaitMsg);

    PRINT '029: wait_payment.waitingMessage updated.';
END
ELSE
    PRINT '029: WARNING — wait_payment node not found.';

-- ── 3. cancel_response: agregar anticipo_amount:null y payment_link_generated:false ──

IF @CancelIdx IS NOT NULL
BEGIN
    -- JSON literal null: JSON_QUERY('null') falla (Msg 13609 — escalar no permitido).
    DECLARE @CancelSetVars029 NVARCHAR(MAX) = JSON_QUERY(
        @Json, N'$.nodes[' + @CancelIdx + N'].config.setVariables');
    IF @CancelSetVars029 IS NOT NULL
    BEGIN
        SET @CancelSetVars029 = JSON_MODIFY(@CancelSetVars029, N'$.anticipo_amount', NULL);
        IF @CancelSetVars029 = N'{}'
            SET @CancelSetVars029 = N'{"anticipo_amount":null}';
        ELSE
            SET @CancelSetVars029 = STUFF(
                @CancelSetVars029,
                LEN(@CancelSetVars029),
                0,
                N',"anticipo_amount":null');
        SET @Json = JSON_MODIFY(
            @Json,
            N'$.nodes[' + @CancelIdx + N'].config.setVariables',
            JSON_QUERY(@CancelSetVars029));
    END;
    SET @Json = JSON_MODIFY(@Json,
        N'$.nodes[' + @CancelIdx + N'].config.setFlags.payment_link_generated',
        CAST(0 AS BIT));
    PRINT '029: cancel_response updated (anticipo_amount=null, payment_link_generated=false).';
END
ELSE
    PRINT '029: WARNING — cancel_response node not found.';

-- ── 4. IntentionSchema: agregar user_says_paid y user_wants_new_link ─────────

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.intentions'))
    WHERE JSON_VALUE(value, N'$.key') = N'user_says_paid'
)
BEGIN
    DECLARE @IntUserPaid NVARCHAR(MAX) = N'{"key":"user_says_paid","description":"El usuario afirma que ya realizó el pago del anticipo","detectionExamples":["ya pagué","listo el pago","ya transferí","hice el pago","ya realicé el pago"],"priority":5,"alwaysDetect":false,"stageCondition":"flag:payment_link_generated","behavior":{"action":"none"}}';
    SET @Json = JSON_MODIFY(@Json, N'append $.intentions', JSON_QUERY(@IntUserPaid));
    PRINT '029: intention user_says_paid added.';
END
ELSE
    PRINT '029: intention user_says_paid already exists — skipping.';

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.intentions'))
    WHERE JSON_VALUE(value, N'$.key') = N'user_wants_new_link'
)
BEGIN
    DECLARE @IntNewLink NVARCHAR(MAX) = N'{"key":"user_wants_new_link","description":"El usuario pide un nuevo link de pago porque el anterior no funciona o expiró","detectionExamples":["otro link","no funciona el link","mándame otro link","nuevo link","el link expiró"],"priority":5,"alwaysDetect":false,"stageCondition":"flag:payment_link_generated","behavior":{"action":"none"}}';
    SET @Json = JSON_MODIFY(@Json, N'append $.intentions', JSON_QUERY(@IntNewLink));
    PRINT '029: intention user_wants_new_link added.';
END
ELSE
    PRINT '029: intention user_wants_new_link already exists — skipping.';

-- ── 5. Rewiring de edges ──────────────────────────────────────────────────────

-- 5a. e12 o e12b: el edge que apunta resolve_pricing (o collect_identity)
--     → show_confirmation debe cambiar a → generate_payment_link

-- Si existe e12b (agregado por 026), redirigirlo
DECLARE @E12bIdx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.edges'))
    WHERE JSON_VALUE(value, N'$.id') = N'e12b'
);

IF @E12bIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        N'$.edges[' + @E12bIdx + N'].targetNodeId', N'generate_payment_link');
    PRINT '029: e12b (resolve_pricing → generate_payment_link) updated.';
END
ELSE
BEGIN
    -- No existe e12b: redirigir e12 directamente
    DECLARE @E12Idx2 NVARCHAR(10) = (
        SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.edges'))
        WHERE JSON_VALUE(value, N'$.id') = N'e12'
    );
    IF @E12Idx2 IS NOT NULL
    BEGIN
        SET @Json = JSON_MODIFY(@Json,
            N'$.edges[' + @E12Idx2 + N'].targetNodeId', N'generate_payment_link');
        PRINT '029: e12 (collect_identity → generate_payment_link) updated.';
    END
END;

-- 5b. e21: generate_payment_link (not_required) → show_confirmation (era create_reservation)
DECLARE @E21Idx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.edges'))
    WHERE JSON_VALUE(value, N'$.id') = N'e21'
);

IF @E21Idx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        N'$.edges[' + @E21Idx + N'].targetNodeId', N'show_confirmation');
    PRINT '029: e21 (generate_payment_link not_required → show_confirmation) updated.';
END
ELSE
    PRINT '029: WARNING — e21 not found.';

-- 5c. e18: reschedule_reservation (skipped) → create_reservation (era generate_payment_link)
DECLARE @E18Idx NVARCHAR(10) = (
    SELECT TOP 1 [key] FROM OPENJSON(JSON_QUERY(@Json, N'$.edges'))
    WHERE JSON_VALUE(value, N'$.id') = N'e18'
);

IF @E18Idx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        N'$.edges[' + @E18Idx + N'].targetNodeId', N'create_reservation');
    PRINT '029: e18 (reschedule_reservation skipped → create_reservation) updated.';
END
ELSE
    PRINT '029: WARNING — e18 not found.';

-- 5d. Nuevo edge: generate_payment_link (skipped) → show_confirmation (reagendamiento)
IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Json, N'$.edges'))
    WHERE JSON_VALUE(value, N'$.id') = N'e21s'
)
BEGIN
    SET @Json = JSON_MODIFY(@Json, N'append $.edges',
        JSON_QUERY(N'{"id":"e21s","sourceNodeId":"generate_payment_link","targetNodeId":"show_confirmation","portId":"skipped"}'));
    PRINT '029: e21s (generate_payment_link skipped → show_confirmation) added.';
END
ELSE
    PRINT '029: e21s already exists — skipping.';

-- ── Guardar ──────────────────────────────────────────────────────────────────

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = SYSUTCDATETIME()
WHERE [FlowDefinitionId] = @FlowDefId;

COMMIT TRANSACTION;
PRINT '029_AnticipoFlowRestructure completed.';
GO
