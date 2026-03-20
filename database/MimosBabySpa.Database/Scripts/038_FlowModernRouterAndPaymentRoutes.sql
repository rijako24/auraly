-- =============================================================================
-- 038: Flujo moderno (Extract + reinicio desde Start) — router principal y pago
--
-- Contexto: con nodo Extract, cada turno el motor reinicia desde Start. Las
-- intenciones locales de wait_payment no bastan; hace falta:
--   1) IntentionRouter tras Extract con ruta flag_true payment_confirmed → create_reservation
--      (webhook Wompi ya pone payment_confirmed en PaymentConfirmationHandler).
--   2) Intenciones globales user_says_paid / user_wants_new_link (textos tomados del
--      diseño de wait_payment en 009) con behavior goto_node → verify_payment /
--      generate_payment_link.
--
-- Además:
--   - Elimina detect_intent y extract_flow_entry (037) si existen.
--   - Inserta start → extract_modern (8) → main_router (9).
--   - Conserva el resto de nodos y aristas del JSON actual (check_availability,
--     resolve_pricing, pago, etc.).
--   - Quita onChange.gotoNode de variables (apuntaban a nodos del grafo antiguo).
--
-- USO: @DryRun = 1 ejecuta y devuelve DefinitionJson en result set; revisar; luego @DryRun = 0.
-- Idempotencia: si ya existe nodo id 'extract_modern' y 'main_router', omite.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun BIT = 1;

-- NULL = buscar por nombre del flujo activo de Mimo
DECLARE @FlowDefinitionId UNIQUEIDENTIFIER = NULL;

IF @FlowDefinitionId IS NULL
    SELECT TOP (1) @FlowDefinitionId = fd.FlowDefinitionId
    FROM dbo.FlowDefinitions AS fd
    WHERE fd.IsActive = 1
      AND fd.Name = N'Flujo Reservas Mimo''s Baby Spa';

IF @FlowDefinitionId IS NULL
BEGIN
    RAISERROR(N'038: No se encontró FlowDefinitionId (ajusta el nombre o asigna el GUID).', 16, 1);
    RETURN;
END;

DECLARE @Json NVARCHAR(MAX);

SELECT @Json = fd.DefinitionJson
FROM dbo.FlowDefinitions AS fd
WHERE fd.FlowDefinitionId = @FlowDefinitionId;

IF @Json IS NULL OR ISJSON(@Json) = 0
BEGIN
    RAISERROR(N'038: DefinitionJson inválido o vacío.', 16, 1);
    RETURN;
END;

IF EXISTS (
    SELECT 1
    FROM OPENJSON(@Json, N'$.nodes')
    WHERE JSON_VALUE(value, N'$.id') IN (N'extract_modern', N'main_router')
)
BEGIN
    PRINT N'038: Ya existen nodos extract_modern/main_router — migración omitida (idempotente).';
    RETURN;
END;

-- ── Cola de nodos (sin start, detect_intent, extract_flow_entry) ─────────────
DECLARE @NodesTail NVARCHAR(MAX) = (
    SELECT STRING_AGG(CAST(j.value AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY TRY_CAST(j.[key] AS INT))
    FROM OPENJSON(@Json, N'$.nodes') AS j
    WHERE JSON_VALUE(j.value, N'$.id') NOT IN (N'start', N'detect_intent', N'extract_flow_entry')
);

IF @NodesTail IS NULL
BEGIN
    RAISERROR(N'038: No quedan nodos tras filtrar — abortar.', 16, 1);
    RETURN;
END;

DECLARE @NodesNew NVARCHAR(MAX) =
      N'['
    + N'{"id":"start","type":0,"label":"Inicio","config":{"_ui":{"x":40,"y":240}}},'
    + N'{"id":"extract_modern","type":8,"label":"Extracción (IA)","config":{"catalogKey":"extract","_ui":{"x":220,"y":240}}},'
    + N'{"id":"main_router","type":9,"label":"Enrutar intención","config":{"routes":[{"when":{"type":"flag_true","flag":"payment_confirmed"},"port":"payment_done"},{"when":"is_information_query","port":"information"}],"defaultPort":"other"},"_ui":{"x":420,"y":240}},'
    + @NodesTail + N']';

IF ISJSON(@NodesNew) = 0
BEGIN
    RAISERROR(N'038: nodes JSON inválido tras concatenar.', 16, 1);
    RETURN;
END;

-- ── Aristas: quitar las que tocan detect_intent o extract_flow_entry ──────────
DECLARE @EdgesTail NVARCHAR(MAX) = (
    SELECT STRING_AGG(CAST(e.value AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY TRY_CAST(e.[key] AS INT))
    FROM OPENJSON(@Json, N'$.edges') AS e
    WHERE JSON_VALUE(e.value, N'$.sourceNodeId') NOT IN (N'detect_intent', N'extract_flow_entry')
      AND JSON_VALUE(e.value, N'$.targetNodeId') NOT IN (N'detect_intent', N'extract_flow_entry')
);

DECLARE @EdgesPrefix NVARCHAR(MAX) =
      N'{"id":"e_038_st_ex","sourceNodeId":"start","targetNodeId":"extract_modern"}'
    + N',{"id":"e_038_ex_mr","sourceNodeId":"extract_modern","targetNodeId":"main_router"}'
    + N',{"id":"e_038_mr_cr","sourceNodeId":"main_router","targetNodeId":"create_reservation","portId":"payment_done"}'
    + N',{"id":"e_038_mr_in","sourceNodeId":"main_router","targetNodeId":"info_response","portId":"information"}'
    + N',{"id":"e_038_mr_cs","sourceNodeId":"main_router","targetNodeId":"collect_service","portId":"other"}';

DECLARE @EdgesNew NVARCHAR(MAX) =
      N'[' + @EdgesPrefix
    + CASE WHEN @EdgesTail IS NULL THEN N'' ELSE N',' + @EdgesTail END + N']';

IF ISJSON(@EdgesNew) = 0
BEGIN
    RAISERROR(N'038: edges JSON inválido.', 16, 1);
    RETURN;
END;

-- info_response debe volver al router (antes volvía a detect_intent)
SET @EdgesNew = REPLACE(@EdgesNew,
                        N'"targetNodeId":"detect_intent"',
                        N'"targetNodeId":"main_router"');

-- ── Base: sustituir nodos y aristas ──────────────────────────────────────────
DECLARE @Out NVARCHAR(MAX) = @Json;
SET @Out = JSON_MODIFY(@Out, N'$.nodes', JSON_QUERY(@NodesNew));
SET @Out = JSON_MODIFY(@Out, N'$.edges', JSON_QUERY(@EdgesNew));

-- ── Añadir intenciones globales (mismos textos que localIntentions en 009) ──
IF CHARINDEX(N'"user_says_paid"', @Out) = 0
    SET @Out = JSON_MODIFY(
        @Out,
        N'append $.intentionSchema',
        JSON_QUERY(
            N'{"key":"user_says_paid","description":"El usuario afirma que ya realizó el pago","examples":["ya pagué","listo el pago","ya transferí","hice el pago"],"priority":6,"behavior":{"action":"goto_node","targetNodeId":"verify_payment"}}'));

IF CHARINDEX(N'"user_wants_new_link"', @Out) = 0
    SET @Out = JSON_MODIFY(
        @Out,
        N'append $.intentionSchema',
        JSON_QUERY(
            N'{"key":"user_wants_new_link","description":"El usuario pide un nuevo link de pago","examples":["otro link","no funciona el link","mándame otro link"],"priority":7,"behavior":{"action":"goto_node","targetNodeId":"generate_payment_link"}}'));

-- ── Quitar gotoNode de variables (evita saltos a nodos eliminados) ───────────
DECLARE @VarCount INT = (SELECT COUNT(*) FROM OPENJSON(JSON_QUERY(@Out, N'$.variables')));
DECLARE @Vi INT = 0;

WHILE @Vi < @VarCount
BEGIN
    SET @Out = JSON_MODIFY(@Out, N'$.variables[' + CAST(@Vi AS NVARCHAR(12)) + N'].onChange.gotoNode', NULL);
    SET @Vi += 1;
END;

IF @DryRun = 1
BEGIN
    PRINT N'038 DRY RUN: OK. FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
    PRINT N'           Nodos y aristas listos; intenciones user_says_paid / user_wants_new_link si faltaban.';
    PRINT N'           Pon @DryRun = 0 para aplicar.';
    PRINT N'           Resultado propuesto (DefinitionJson) en el result set siguiente:';
    SELECT @Out AS DefinitionJson;
    RETURN;
END;

UPDATE dbo.FlowDefinitions
SET DefinitionJson = @Out,
    UpdatedAt      = GETUTCDATE()
WHERE FlowDefinitionId = @FlowDefinitionId;

PRINT N'038: Actualizado FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
GO
