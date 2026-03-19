-- =============================================================================
-- Migration 017: Unified Intention + Extraction (Single LLM Call)
-- =============================================================================
-- Changes applied to the FlowDefinition JSON:
--
--  1. Replaces intentions[] with intentionSchema[] — 7 global intentions
--     detected as boolean flags inside the extraction LLM call (no extra call).
--
--  2. detect_intent: type 3 (LLMClassify) → type 9 (IntentionRouter)
--     Routes via is_information_query flag; no LLM call.
--
--  3. detect_confirmation: type 3 (LLMClassify) → type 9 (IntentionRouter)
--     Routes via user_confirmed_booking / user_wants_to_cancel flags; no LLM call.
--
--  4. Removes applyGuards from service, desired_date, desired_time variables.
--     Guards are no longer needed: the extraction prompt instructs the LLM
--     not to extract transactional fields when is_information_query=true.
--
--  5. extractionMaxTokens: 600 → 900 (unified response includes intentions block).
--
-- Result: 2 LLM calls per turn (extraction+intentions, then response).
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @FlowDefId UNIQUEIDENTIFIER = (
    SELECT TOP 1 [FlowDefinitionId] FROM [dbo].[FlowDefinitions] ORDER BY [CreatedAt] DESC
);

DECLARE @DefinitionJson NVARCHAR(MAX) = (
    SELECT [DefinitionJson] FROM [dbo].[FlowDefinitions] WHERE [FlowDefinitionId] = @FlowDefId
);

IF @DefinitionJson IS NULL
BEGIN
    RAISERROR('FlowDefinition not found', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @SchemaCount INT;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Replace intentions[] with intentionSchema[]
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @IntentionSchema NVARCHAR(MAX) = N'['
+ N'{"key":"user_wants_human_assistance","description":"El usuario quiere hablar con una persona real, asesor o agente humano","examples":["quiero hablar con alguien","un asesor","un humano","quiero una persona","me comunicas con alguien"],"degradedRegex":"(?i)(hablar con|asesor|humano|persona real|agente|operador|ayuda de verdad|quiero.*persona)","priority":1,"behavior":{"action":"escalate","responseTemplate":"Con gusto te conecto con una de nuestras asesoras. Un momento por favor. \ud83d\udc95"}},'
+ N'{"key":"user_wants_to_cancel","description":"El usuario quiere cancelar el proceso actual, no quiere continuar o pide empezar de nuevo","examples":["cancelar","no quiero","d\u00e9jalo","ol\u00eddalo","ya no quiero","no gracias"],"degradedRegex":"(?i)(cancelar|no quiero|d\u00e9jalo|olv\u00edd|ya no quiero)","priority":2,"behavior":{"action":"goto_node","targetNodeId":"cancel_response"}},'
+ N'{"key":"user_wants_to_reschedule","description":"El usuario quiere cambiar, mover o reagendar una cita o reserva existente","examples":["cambiar mi cita","reagendar","mover mi reserva","otro d\u00eda","cambiar la fecha"],"priority":3,"behavior":{"action":"goto_node","targetNodeId":"reschedule_setup"}},'
+ N'{"key":"user_wants_to_hold","description":"El usuario quiere pausar o poner en espera una reserva sin cancelarla","examples":["ponlo en espera","no puedo ir pero no canceles","pausar","suspender la cita"],"priority":4,"behavior":{"action":"goto_node","targetNodeId":"hold_handler"}},'
+ N'{"key":"user_requested_availability","description":"El usuario pregunta expl\u00edcitamente por disponibilidad, horarios disponibles o cupos, sin necesariamente querer reservar a\u00fan","examples":["\u00bfqu\u00e9 horarios tienen?","\u00bfhay disponibilidad?","\u00bftienen para el viernes?","\u00bfcu\u00e1ndo hay espacio?"],"priority":10,"behavior":{"action":"none"}},'
+ N'{"key":"is_information_query","description":"El usuario pregunta sobre servicios, precios, beneficios, planes o informaci\u00f3n general del spa. NO aplicar si el usuario ya eligi\u00f3 un servicio o est\u00e1 en proceso de agendar","examples":["\u00bfqu\u00e9 servicios tienen?","\u00bfcu\u00e1nto cuesta?","cu\u00e9ntame sobre el Plan Marineritos","\u00bfqu\u00e9 incluye?","informaci\u00f3n sobre los planes"],"priority":10,"behavior":{"action":"none"}},'
+ N'{"key":"user_confirmed_booking","description":"El usuario confirma expl\u00edcitamente que los datos del resumen est\u00e1n correctos y quiere proceder. Solo evaluar cuando se ha presentado el resumen de reserva.","examples":["s\u00ed","confirmo","est\u00e1 bien","as\u00ed est\u00e1 bien","confirmado","perfecto as\u00ed"],"stageCondition":"flag:confirmation_summary_presented","priority":10,"behavior":{"action":"none"}}'
+ N']';

-- Remove old intentions[] key and insert intentionSchema[]
SET @DefinitionJson = JSON_MODIFY(@DefinitionJson, '$.intentions', NULL);
SET @DefinitionJson = JSON_MODIFY(@DefinitionJson, '$.intentionSchema', JSON_QUERY(@IntentionSchema));

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. detect_intent: LLMClassify → IntentionRouter
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @DetectIntentIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@DefinitionJson, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'detect_intent'
);

IF @DetectIntentIdx IS NULL
BEGIN
    RAISERROR('Node detect_intent not found in flow definition', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

SET @DefinitionJson = JSON_MODIFY(
    @DefinitionJson,
    '$.nodes[' + @DetectIntentIdx + '].type',
    9
);
SET @DefinitionJson = JSON_MODIFY(
    @DefinitionJson,
    '$.nodes[' + @DetectIntentIdx + '].config',
    JSON_QUERY(N'{"routes":[{"when":"is_information_query","port":"information"}],"defaultPort":"other"}')
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. detect_confirmation: LLMClassify → IntentionRouter
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @DetectConfirmIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@DefinitionJson, '$.nodes'))
    WHERE JSON_VALUE(value, '$.id') = 'detect_confirmation'
);

IF @DetectConfirmIdx IS NULL
BEGIN
    RAISERROR('Node detect_confirmation not found in flow definition', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

SET @DefinitionJson = JSON_MODIFY(
    @DefinitionJson,
    '$.nodes[' + @DetectConfirmIdx + '].type',
    9
);
SET @DefinitionJson = JSON_MODIFY(
    @DefinitionJson,
    '$.nodes[' + @DetectConfirmIdx + '].config',
    JSON_QUERY(N'{"routes":[{"when":"user_wants_to_cancel","port":"cancel"},{"when":"user_confirmed_booking","port":"confirmed"}],"defaultPort":"wants_changes"}')
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. Remove applyGuards from service, desired_date, desired_time
--    (the extraction prompt now instructs the LLM not to extract these
--     when is_information_query=true — no code-level guards needed)
-- ─────────────────────────────────────────────────────────────────────────────

DECLARE @ServiceVarIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@DefinitionJson, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'service'
);

DECLARE @DesiredDateIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@DefinitionJson, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'desired_date'
);

DECLARE @DesiredTimeIdx NVARCHAR(10) = (
    SELECT TOP 1 [key]
    FROM OPENJSON(JSON_QUERY(@DefinitionJson, '$.variables'))
    WHERE JSON_VALUE(value, '$.key') = 'desired_time'
);

IF @ServiceVarIdx IS NOT NULL
    SET @DefinitionJson = JSON_MODIFY(
        @DefinitionJson,
        '$.variables[' + @ServiceVarIdx + '].applyGuards',
        JSON_QUERY(N'[]')
    );

IF @DesiredDateIdx IS NOT NULL
    SET @DefinitionJson = JSON_MODIFY(
        @DefinitionJson,
        '$.variables[' + @DesiredDateIdx + '].applyGuards',
        JSON_QUERY(N'[]')
    );

IF @DesiredTimeIdx IS NOT NULL
    SET @DefinitionJson = JSON_MODIFY(
        @DefinitionJson,
        '$.variables[' + @DesiredTimeIdx + '].applyGuards',
        JSON_QUERY(N'[]')
    );

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Bump extractionMaxTokens: 600 → 900
--    (unified JSON response includes extracted_fields + intentions + ambiguities)
-- ─────────────────────────────────────────────────────────────────────────────

SET @DefinitionJson = JSON_MODIFY(
    @DefinitionJson,
    '$.engineSettings.extractionMaxTokens',
    900
);

-- ─────────────────────────────────────────────────────────────────────────────
-- Apply changes
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @DefinitionJson,
    [UpdatedAt]     = GETUTCDATE()
WHERE [FlowDefinitionId] = @FlowDefId;

-- Verify the result
DECLARE @SchemaCount INT;
SELECT @SchemaCount = COUNT(*) FROM OPENJSON(JSON_QUERY(@DefinitionJson, '$.intentionSchema'));
PRINT 'Migration 017 completed successfully.';
PRINT 'detect_intent type = '      + CAST(JSON_VALUE(@DefinitionJson, '$.nodes[' + @DetectIntentIdx + '].type') AS NVARCHAR(5));
PRINT 'intentionSchema count = '   + CAST(@SchemaCount AS NVARCHAR(5));
PRINT 'extractionMaxTokens = '     + ISNULL(JSON_VALUE(@DefinitionJson, '$.engineSettings.extractionMaxTokens'), 'NULL');

COMMIT TRANSACTION;
GO
