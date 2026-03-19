-- =============================================================================
-- Migration 021: Fix offer_addons node type + selected_add_ons extraction hint
--
-- Root-cause: offer_addons was GenerateResponse (type 4), which fires once and
-- relies on executeWhen + setFlags to skip itself on re-entry. That workaround
-- is too aggressive: it skips the node on ANY re-visit, including when the user
-- asks a follow-up question mid-offer ("dame más info sobre las decoraciones"),
-- causing the engine to fall through to collect_date and generate a nonsensical
-- response ("Si te interesa, podríamos agendar...") despite the service already
-- being selected.
--
-- Fix:
--   1. offer_addons type 4 (GenerateResponse) → type 1 (CollectFields)
--      CollectFields re-executes naturally as long as selected_add_ons is null,
--      and advances automatically once the field is filled. No executeWhen needed.
--
--   2. Remove executeWhen + setFlags + add_ons_offered flag workaround from config.
--      CollectFields manages the "already offered" state via the variable itself.
--
--   3. Fix selected_add_ons extraction hint:
--      "no extraer nada" on rejection causes null → CollectFields would loop forever.
--      A deliberate rejection is a decision, not silence — extract "ninguno" so the
--      engine can advance.
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

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. offer_addons: GenerateResponse (type 4) → CollectFields (type 1)
--    New config:
--      fields: ["selected_add_ons"]
--      instructions: same conversational text, adapted for CollectFields context
--    Removed: executeWhen, setFlags (add_ons_offered workaround no longer needed)
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @OfferAddonsIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'offer_addons'
);

IF @OfferAddonsIdx IS NULL
BEGIN
    RAISERROR('Node offer_addons not found in flow definition', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

SET @Json = JSON_MODIFY(@Json, '$.nodes[' + @OfferAddonsIdx + '].type', 1);

SET @Json = JSON_MODIFY(
    @Json,
    '$.nodes[' + @OfferAddonsIdx + '].config',
    JSON_QUERY(N'{
      "fields": ["selected_add_ons"],
      "instructions": "El cliente eligió el servicio indicado en DATOS RECOPILADOS. De forma natural y sin presionar, menciona los complementos opcionales disponibles (Cumplemes): decoración sencilla ($35.000) y bouquet personalizado ($55.000). Pregunta si desea agregar alguno. Si hace preguntas sobre los complementos, respóndelas con detalle y vuelve a preguntar su decisión. Solo avanza cuando el cliente indique su elección o rechace explícitamente."
    }')
);

PRINT 'offer_addons: type changed to CollectFields and config updated.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Fix selected_add_ons extraction hint
--    Old: "Si rechaza o dice ninguno no extraer nada" → null → CollectFields loops
--    New: "Si rechaza o dice ninguno extraer: ninguno" → field filled → Advance
-- ─────────────────────────────────────────────────────────────────────────────

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
        N'Complemento Cumplemes seleccionado. '
        + N'Si menciona sencilla o decoracion sencilla extraer: Cumplemes decoracion sencilla. '
        + N'Si menciona personalizada o bouquet extraer: Cumplemes decoracion personalizada. '
        + N'Si rechaza o dice ninguno o no quiere extraer: ninguno.'
    );
    PRINT 'selected_add_ons: extractionHint updated.';
END
ELSE
    PRINT 'WARNING: variable selected_add_ons not found — extractionHint not updated.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Remove add_ons_offered from sessionConfig.transactionalVariables if present
--    The flag was only needed for the executeWhen workaround.
--    NOTE: The flag itself (add_ons_offered) in FlagsJson is harmless to keep —
--    no node reads it anymore. Only cleaning transactionalVariables references
--    would require rebuilding the JSON array; skipping to keep migration simple.
-- ─────────────────────────────────────────────────────────────────────────────

-- ─────────────────────────────────────────────────────────────────────────────
-- Apply changes
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE [dbo].[FlowDefinitions]
SET    [DefinitionJson] = @Json,
       [UpdatedAt]      = GETUTCDATE()
WHERE  [FlowDefinitionId] = @FlowDefId;

-- Verify
DECLARE @NodeType NVARCHAR(5);
SET @NodeType = JSON_VALUE(@Json, '$.nodes[' + @OfferAddonsIdx + '].type');

DECLARE @HintSnippet NVARCHAR(50);
SET @HintSnippet = LEFT(ISNULL(JSON_VALUE(@Json, '$.variables[' + ISNULL(@AddOnsVarIdx,'0') + '].extractionHint'), 'N/A'), 50);

PRINT '021_OfferAddonsNodeFix completed successfully.';
PRINT 'offer_addons node type = ' + ISNULL(@NodeType, 'NULL') + ' (expected: 1)';
PRINT 'selected_add_ons hint  = ' + @HintSnippet + '...';

COMMIT TRANSACTION;
GO
