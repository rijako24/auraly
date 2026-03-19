-- =============================================================================
-- Migration 018: Flow Graph Restructure
-- =============================================================================
-- Root-cause fixes (no workarounds, no redundancy):
--
--  1. Add variable selected_add_ons (group: business)
--     → Extraction captures "sí la sencilla" → "Cumplemes decoración sencilla"
--
--  2. Clean user_requested_availability examples (remove date-contaminated sample
--     "¿tienen para el viernes?" that caused the LLM to absorb dates as intention
--     evidence instead of extracting them as desired_date).
--
--  3. collect_date.fields → ["desired_date"] only
--     → Allows user to trigger availability check without specifying a time first.
--     Dates + times provided together are still captured by the extraction service.
--
--  4. show_alternatives → CollectFields (type 1) for desired_time
--     → After showing available slots the node waits for time selection;
--       advances only when desired_time is filled; prevents re-showing slots.
--
--  5. Edge e11: show_alternatives → collect_date becomes show_alternatives → check_availability
--     → After time selection, go directly to availability verification.
--
--  6. cancel_response: reset selected_add_ons
--
--  7. show_confirmation: use {{collected_data}} token (handles optional fields like
--     selected_add_ons cleanly — shows only when non-null).
--
--  8. transactionalVariables: include selected_add_ons
--
--  9. user_requested_availability behavior: action=none → goto_node: collect_date
--     → The intention was correctly detected but silently ignored. Now triggers
--       the availability check path. If desired_date is already extracted that
--       turn, collect_date advances immediately (zero extra round-trips).
--
-- 10. extractionInstructions: append contextual disambiguation rule
--     → "thomas" after "¿cómo se llama tu bebé?" → baby_name (not customer_name).
--       The bot's preceding question now has priority over field similarity.
--
-- 11. collect_service instructions: always list categories explicitly on first msg
--     → Prohibits vague "algún otro servicio"; names Planes Baby Spa, Talleres,
--       Materno Spa, Dulce Espera, Iniciación al Jardín.
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
-- 1. Add variable: selected_add_ons
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @SelectedAddOnsVar NVARCHAR(MAX) = N'{'
+ N'"key":"selected_add_ons",'
+ N'"label":"Servicio adicional",'
+ N'"dataType":"String",'
+ N'"required":false,'
+ N'"group":"business",'
+ N'"displayOrder":9,'
+ N'"showInSummary":true,'
+ N'"isSystemManaged":false,'
+ N'"extractionHint":"Complemento Cumplemes seleccionado. '
+ N'Si menciona sencilla o decoracion sencilla extraer: Cumplemes decoracion sencilla. '
+ N'Si menciona personalizada o decoracion personalizada extraer: Cumplemes decoracion personalizada. '
+ N'Si rechaza o dice ninguno no extraer nada."'
+ N'}';

-- Only add if not already present
DECLARE @ExistingAddOns NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'selected_add_ons'
);

IF @ExistingAddOns IS NULL
    SET @Json = JSON_MODIFY(@Json, 'append $.variables', JSON_QUERY(@SelectedAddOnsVar));

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Fix user_requested_availability examples (remove date-contaminated sample)
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @AvailIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.intentionSchema'))
    WHERE JSON_VALUE(value, '$.key') = 'user_requested_availability'
);

IF @AvailIdx IS NOT NULL
    SET @Json = JSON_MODIFY(
        @Json,
        '$.intentionSchema[' + @AvailIdx + '].examples',
        JSON_QUERY(N'["¿qué horarios tienen?","¿hay disponibilidad?","¿tienen cupo?","¿cuándo hay espacio disponible?"]')
    );

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. collect_date.fields → ["desired_date"] only
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @CollectDateIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'collect_date'
);

IF @CollectDateIdx IS NOT NULL
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @CollectDateIdx + '].config.fields',
        JSON_QUERY(N'["desired_date"]')
    );

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. show_alternatives: GenerateResponse (type 4) → CollectFields (type 1)
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @ShowAltIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'show_alternatives'
);

IF @ShowAltIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json, '$.nodes[' + @ShowAltIdx + '].type', 1);
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @ShowAltIdx + '].config',
        JSON_QUERY(N'{"fields":["desired_time"],"instructions":"Los horarios disponibles para la fecha solicitada son: {{variables.available_time_slots}}. Preséntale las opciones de forma amigable y pide que elija uno. El sistema registrará su selección automáticamente."}')
    );
END;

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Edge e11: show_alternatives → collect_date becomes show_alternatives → check_availability
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @E11Idx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.edges'))
    WHERE JSON_VALUE(value, '$.id') = 'e11'
);

IF @E11Idx IS NOT NULL
    SET @Json = JSON_MODIFY(
        @Json,
        '$.edges[' + @E11Idx + '].targetNodeId',
        'check_availability'
    );

-- ─────────────────────────────────────────────────────────────────────────────
-- 6. cancel_response: reset selected_add_ons
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @CancelIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'cancel_response'
);

-- JSON_QUERY('null') causes "Unexpected character 'n'" in some SQL Server versions,
-- so we add selected_add_ons via string replacement on the setVariables object.
IF @CancelIdx IS NOT NULL
BEGIN
    DECLARE @SetVars NVARCHAR(MAX) = JSON_QUERY(@Json, '$.nodes[' + @CancelIdx + '].config.setVariables');
    IF @SetVars IS NOT NULL AND @SetVars NOT LIKE N'%selected_add_ons%'
    BEGIN
        SET @SetVars = REPLACE(@SetVars, N'}', N',"selected_add_ons":null}');
        SET @Json = JSON_MODIFY(@Json, '$.nodes[' + @CancelIdx + '].config.setVariables', JSON_QUERY(@SetVars));
    END;
END;

-- ─────────────────────────────────────────────────────────────────────────────
-- 7. show_confirmation: use {{collected_data}} to handle optional fields cleanly
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @ConfirmIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'show_confirmation'
);

IF @ConfirmIdx IS NOT NULL
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @ConfirmIdx + '].config.instructions',
        N'📋 *Resumen de tu reserva:*' + CHAR(10) + CHAR(10)
        + N'{{collected_data}}' + CHAR(10) + CHAR(10)
        + N'¿Confirmas estos datos? ✅'
    );

-- ─────────────────────────────────────────────────────────────────────────────
-- 8. sessionConfig.transactionalVariables: add selected_add_ons
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @AlreadyTransactional NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.sessionConfig.transactionalVariables'))
    WHERE value = 'selected_add_ons'
);

IF @AlreadyTransactional IS NULL
    SET @Json = JSON_MODIFY(@Json, 'append $.sessionConfig.transactionalVariables', 'selected_add_ons');

-- ─────────────────────────────────────────────────────────────────────────────
-- 9. user_requested_availability: behavior → goto_node → collect_date
--    The intention was detected but action=none meant the engine ignored it.
--    With goto_node, when user asks for availability the engine jumps to collect_date.
--    If desired_date is already extracted that turn, collect_date advances immediately
--    to check_availability in the same turn (zero extra round-trips).
-- ─────────────────────────────────────────────────────────────────────────────

-- @AvailIdx was declared in change #2 — reuse it here
IF @AvailIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json, '$.intentionSchema[' + @AvailIdx + '].behavior.action',       'goto_node');
    SET @Json = JSON_MODIFY(@Json, '$.intentionSchema[' + @AvailIdx + '].behavior.targetNodeId', 'collect_date');
END;

-- ─────────────────────────────────────────────────────────────────────────────
-- 10. extractionInstructions: append contextual disambiguation rule
--     When user replies with a single value the bot just asked for,
--     the preceding question must determine which field it belongs to.
--     Without this rule the LLM maps "thomas" to customer_name even when
--     the bot asked "¿cómo se llama tu bebé?".
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @CurrentInstr NVARCHAR(MAX) = JSON_VALUE(@Json, '$.extractionInstructions');
IF @CurrentInstr IS NOT NULL
BEGIN
    DECLARE @DisambigRule NVARCHAR(MAX) =
        CHAR(10) + CHAR(10)
        + N'Regla de desambiguación contextual:' + CHAR(10)
        + N'- Si el usuario responde con un único dato simple (nombre, hora, número) y el último'  + CHAR(10)
        + N'  mensaje del asistente contenía una pregunta específica, usa esa pregunta para'       + CHAR(10)
        + N'  determinar a qué campo pertenece el dato:' + CHAR(10)
        + N'    · "¿cómo se llama tu bebé?" / "nombre de tu bebé" → baby_name' + CHAR(10)
        + N'    · "¿cuál es tu nombre?" / "tu nombre" → customer_name'         + CHAR(10)
        + N'    · "¿a qué hora?" / "hora preferida" → desired_time'            + CHAR(10)
        + N'    · "¿qué fecha?" / "qué día" → desired_date'                    + CHAR(10)
        + N'- La pregunta del asistente tiene PRIORIDAD sobre la similitud semántica del campo.';
    SET @Json = JSON_MODIFY(@Json, '$.extractionInstructions', @CurrentInstr + @DisambigRule);
END;

-- ─────────────────────────────────────────────────────────────────────────────
-- 11. collect_service: instructions explicitly list service categories
--     Old instructions said "invita a contar qué necesitan" — too vague.
--     New: always name categories on first message + prohibit "algún otro servicio".
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @CollectSvcIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'collect_service'
);

IF @CollectSvcIdx IS NOT NULL
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @CollectSvcIdx + '].config.instructions',
        N'El sistema indica si es el primer mensaje de la sesión en el "CONTEXTO DE SESIÓN".'
        + CHAR(10) + CHAR(10)
        + N'▸ PRIMER MENSAJE (Primera interacción de la sesión: Sí):'                                                   + CHAR(10)
        + N'  PRIORIDAD ABSOLUTA — no preguntes por el servicio ni por ningún dato todavía.'                             + CHAR(10)
        + N'  - Saluda con calidez y preséntate (quién eres, de qué negocio).'                                          + CHAR(10)
        + N'  - Si el mensaje trae edad del bebé u otro contexto, reconócelo en la respuesta.'                           + CHAR(10)
        + N'  - Presenta SIEMPRE las categorías disponibles de forma breve:'                                             + CHAR(10)
        + N'    Planes Baby Spa, Talleres de Estimulación Temprana (grupales y personalizados),'                         + CHAR(10)
        + N'    Materno Spa, Dulce Espera, Programa Iniciación al Jardín.'                                               + CHAR(10)
        + N'  - Si conoces la edad del bebé, menciona qué categorías se adaptan mejor'                                   + CHAR(10)
        + N'    (ej: 5 meses → Planes Baby Spa, Talleres Grupales grupo Pulpitos).'                                      + CHAR(10)
        + N'  - NUNCA digas "algún otro servicio" ni "algún servicio en particular" —'                                   + CHAR(10)
        + N'    el cliente no sabe qué existe; debes nombrar las opciones explícitamente.'                                + CHAR(10)
        + CHAR(10)
        + N'▸ CLIENTE RECURRENTE (Cliente recurrente: Sí, sesión previa registrada):'                                    + CHAR(10)
        + N'  - NO te presentes de nuevo.'                                                                               + CHAR(10)
        + N'  - Saluda reconociendo que regresa.'                                                                        + CHAR(10)
        + N'  - Si solo saluda → pregunta en qué puedes ayudarle. No presentes catálogo sin que lo pida.'               + CHAR(10)
        + N'  - Si trae pregunta o datos → respóndelos después del saludo.'                                              + CHAR(10)
        + CHAR(10)
        + N'▸ CONVERSACIÓN EN CURSO (Primera interacción de la sesión: No):'                                             + CHAR(10)
        + N'  - Si el usuario NO pregunta por un plan concreto → presenta las CATEGORÍAS:'                               + CHAR(10)
        + N'    Planes Baby Spa, Talleres de Estimulación Temprana, Materno Spa, Dulce Espera, Iniciación al Jardín.'    + CHAR(10)
        + N'    Si conoces la edad, indica cuál se adapta. NUNCA digas "algún otro servicio".'                           + CHAR(10)
        + N'  - Si pregunta por UN plan o servicio concreto → detalla SOLO ese plan (incluye, beneficios, precio).'      + CHAR(10)
        + N'  - Si acepta un plan recomendado ("sí ese", "está bien") → procede al siguiente paso.'                      + CHAR(10)
        + N'  - NO preguntes fecha, hora ni datos personales — eso va después de elegir servicio.'
    );

-- ─────────────────────────────────────────────────────────────────────────────
-- Apply changes
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json,
    [UpdatedAt]      = GETUTCDATE()
WHERE [FlowDefinitionId] = @FlowDefId;

-- Verify
DECLARE @VarCount INT;
SELECT @VarCount = COUNT(*) FROM OPENJSON(JSON_QUERY(@Json, '$.variables'));

DECLARE @ShowAltType NVARCHAR(5);
SET @ShowAltType = JSON_VALUE(@Json, '$.nodes[' + @ShowAltIdx + '].type');

DECLARE @E11Target NVARCHAR(100);
SET @E11Target = JSON_VALUE(@Json, '$.edges[' + @E11Idx + '].targetNodeId');

PRINT 'Migration 018 completed successfully.';
PRINT 'variables count = '          + CAST(@VarCount AS NVARCHAR(5));
PRINT 'show_alternatives type = '   + ISNULL(@ShowAltType, 'NULL');
PRINT 'edge e11 target = '          + ISNULL(@E11Target, 'NULL');
PRINT 'selected_add_ons in vars = ' + ISNULL(@ExistingAddOns, 'newly added');

COMMIT TRANSACTION;
GO
