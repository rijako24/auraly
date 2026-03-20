-- =============================================================================
-- 041: Migrar a arquitectura de Cluster Nodes (subNodes + routingIntents)
--
-- Funciona desde CUALQUIER estado previo:
--   - DB original (009): ~24 nodos con detect_intent, collect_service, etc.
--   - DB intermedia (037-040): extract_modern, main_router, agent_reserva, etc.
--
-- Cambios:
--   1. Reemplaza TODOS los nodos y aristas con 16 nodos cluster-node
--   2. Agrega routingIntents al nivel del flujo (clasificación del Router)
--   3. Router con clasificación LLM integrada (3 fases)
--   4. Cada Agent recibe subNodes explícitos (extract, actions, knowledge)
--   5. Migra FlowExecutionStates activos (remapeo completo de nodos viejos)
--   6. Preserva variables, engineSettings, extractionInstructions, sessionConfig
--
-- Nodos resultantes (16):
--   start, router, agent_info, agent_cancelar, agent_reservas, agent_reagendar,
--   accept_booking, generate_payment_link, wait_payment, verify_payment,
--   payment_not_found, create_reservation, success_response, hold_handler,
--   escalate, end
--
-- Prerequisitos: 035_FlowNodeCatalog.sql y 036_FlowModernNodes.sql
--
-- @DryRun = 1 -> SELECT sin actualizar BD.
-- @DryRun = 0 -> UPDATE FlowDefinitions + FlowExecutionStates.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun BIT = 1;

DECLARE @FlowDefinitionId UNIQUEIDENTIFIER = NULL;

IF @FlowDefinitionId IS NULL
    SELECT TOP (1) @FlowDefinitionId = fd.FlowDefinitionId
    FROM dbo.FlowDefinitions fd
    WHERE fd.IsActive = 1
      AND fd.Name = N'Flujo Reservas Mimo''s Baby Spa';

IF @FlowDefinitionId IS NULL
BEGIN
    RAISERROR(N'041: No se encontro FlowDefinitionId.', 16, 1);
    RETURN;
END;

DECLARE @Json NVARCHAR(MAX);
SELECT @Json = fd.DefinitionJson
FROM dbo.FlowDefinitions fd
WHERE fd.FlowDefinitionId = @FlowDefinitionId;

IF @Json IS NULL OR ISJSON(@Json) = 0
BEGIN
    RAISERROR(N'041: DefinitionJson invalido o vacio.', 16, 1);
    RETURN;
END;

-- =============================================================================
-- ROUTING INTENTS (flow-level, para clasificación del Router y escape intents)
-- =============================================================================

DECLARE @RoutingIntents NVARCHAR(MAX) = N'[' +
    N'{"key":"is_information_query","description":"El usuario pregunta por informacion, servicios, precios, beneficios o planes","examples":["que servicios tienen","cuanto cuesta","que planes hay"]},' +
    N'{"key":"user_wants_to_reserve","description":"El usuario quiere hacer una reserva o cita","examples":["quiero reservar","quiero una cita","quiero agendar"]},' +
    N'{"key":"user_wants_to_cancel","description":"El usuario quiere cancelar su proceso o reserva","examples":["quiero cancelar","ya no quiero","cancela"]},' +
    N'{"key":"user_wants_to_reschedule","description":"El usuario quiere cambiar la fecha/hora de su reserva","examples":["quiero reagendar","cambiar la fecha","mover mi cita"]},' +
    N'{"key":"user_wants_to_hold","description":"El usuario quiere pausar su reserva","examples":["ponla en pausa","no la canceles pero pausala"]}' +
N']';

-- =============================================================================
-- NODOS - 16 nodos con cluster architecture
-- =============================================================================

DECLARE @NodesNew NVARCHAR(MAX);
SET @NodesNew = CAST(N'' AS NVARCHAR(MAX)) + N'['

-- ---- Start ------------------------------------------------------------------
+ N'{"id":"start","type":0,"label":"Inicio","config":{"_ui":{"x":50,"y":400}}},'

-- ---- Router (type 9) con clasificación integrada ----------------------------
+ N'{"id":"router","type":9,"label":"Router","config":{'
    + N'"classification":{"instructions":"Clasifica la intencion del usuario basandote en su mensaje. Si el usuario saluda o su intencion no es clara, usa el defaultPort."},'
    + N'"routes":['
        + N'{"when":{"type":"flag_true","flag":"payment_confirmed"},"port":"payment_done"},'
        + N'{"when":"is_information_query","port":"information"},'
        + N'{"when":"user_wants_to_cancel","port":"cancel"},'
        + N'{"when":"user_wants_to_reschedule","port":"reschedule"},'
        + N'{"when":"user_wants_to_hold","port":"hold"},'
        + N'{"when":"user_wants_to_reserve","port":"reserva"}'
    + N'],'
    + N'"defaultPort":"reserva",'
    + N'"_ui":{"x":300,"y":400}'
+ N'}},'

-- ---- Agent Info (type 10, cluster con subNodes) -----------------------------
+ N'{"id":"agent_info","type":10,"label":"Agente Info","handlesIntent":"is_information_query","config":{'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"Responde la consulta informativa del usuario usando el catalogo disponible.",'
    + N'"completionPort":"completed",'
    + N'"_ui":{"x":700,"y":40}'
+ N'},"subNodes":{'
    + N'"extract":{"id":"ext_info","label":"Extraccion Info","slot":0,"config":{"fields":[]}},'
    + N'"knowledge":[{"id":"ks_catalogo","label":"Catalogo","slot":2,"config":{"knowledgeSourceId":"65D838C3-0FC2-41B9-822C-EDD479E545F8"}},{"id":"ks_faq","label":"FAQ","slot":2,"config":{"knowledgeSourceId":"BBD27A73-02D7-4F8D-A80A-2928F3A8BC03"}}]'
+ N'}},'

-- ---- Agent Cancelar (type 10, cluster con subNodes) -------------------------
+ N'{"id":"agent_cancelar","type":10,"label":"Agente Cancelar","handlesIntent":"user_wants_to_cancel","config":{'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"El cliente decidio no continuar con el proceso de reserva.\r\n- Acepta sin presionar ni insistir.\r\n- Agradece su tiempo con calidez.\r\n- Ofrece comenzar de nuevo cuando lo desee.",'
    + N'"completionPort":"completed",'
    + N'"_ui":{"x":220,"y":40}'
+ N'},"subNodes":{'
    + N'"extract":{"id":"ext_cancelar","label":"Extraccion Cancelar","slot":0,"config":{"fields":[]}}'
+ N'}},'

-- ---- Agent Reservas (type 10, cluster con extract + actions) ----------------
+ N'{"id":"agent_reservas","type":10,"label":"Agente Reservas","handlesIntent":"user_wants_to_reserve","config":{'
    + N'"collect":{'
        + N'"fields":["service","desired_date","desired_time","selected_add_ons","customer_name","email","baby_name","baby_age"],'
        + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8"],'
        + N'"instructions":"El sistema indica si es el primer mensaje en CONTEXTO DE SESION.\r\n\r\n> PRIMER MENSAJE (Primera interaccion: Si):\r\n  - Saluda con calidez y presentate.\r\n  - Presenta SIEMPRE las categorias: Planes Baby Spa, Talleres Estimulacion Temprana, Materno Spa, Dulce Espera, Programa Iniciacion al Jardin.\r\n  - Si conoces la edad del bebe, menciona que categorias se adaptan mejor.\r\n  - NUNCA digas <<algun otro servicio>> - nombra las opciones explicitamente.\r\n\r\n> CLIENTE RECURRENTE (Cliente recurrente: Si):\r\n  - NO te presentes de nuevo. Saluda reconociendo que regresa.\r\n  - Si solo saluda -> pregunta en que puedes ayudarle.\r\n\r\n> RECOLECCION DE DATOS (conversacion en curso):\r\n  - Pide datos UNO A LA VEZ, de forma conversacional y calida.\r\n  - Orden natural: 1) servicio -> 2) fecha y hora -> 3) extras Cumplemes (SOLO despues de confirmar disponibilidad) -> 4) nombre, email, bebe.\r\n  - Si la disponibilidad fue confirmada en este turno: ofrece Cumplemes naturalmente (sencilla $35.000 o bouquet $55.000).\r\n  - Cuando haya horarios disponibles (no exactos): presentalos y pide que elija.\r\n  - NO pidas datos personales antes de confirmar disponibilidad y extras.\r\n  - NO inventes precios ni informacion - usa el catalogo disponible."'
    + N'},'
    + N'"responseMode":"llm",'
    + N'"instructions":"ESTADO: TODOS LOS DATOS RECOPILADOS Y PRECIO CALCULADO.\r\nPresenta el resumen completo de la reserva y pide confirmacion al cliente.\r\n\r\n*Resumen de tu reserva:*\r\n{{collected_data}}\r\n\r\nConfirmas todos estos datos para proceder con el pago?\r\n(Responde <<Si>> para confirmar o cuentame que deseas cambiar.)",'
    + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8"],'
    + N'"completionBehavior":"respond",'
    + N'"completionPort":"completed",'
    + N'"waitForUser":true,'
    + N'"_ui":{"x":700,"y":220}'
+ N'},"subNodes":{'
    + N'"extract":{"id":"ext_reservas","label":"Extraccion Reservas","slot":0,"config":{"fields":["service","desired_date","desired_time","selected_add_ons","customer_name","email","baby_name","baby_age"]}},'
    + N'"actions":['
        + N'{"id":"act_check_avail","label":"Verificar disponibilidad","slot":1,"config":{'
            + N'"action_type":"check_availability",'
            + N'"input_mapping":{"item":"{{variables.service}}","date":"{{variables.desired_date}}","time":"{{variables.desired_time}}"},'
            + N'"output_mapping":{"available_time_slots":"slots","flag:availability_confirmed":"has_exact_match","desired_time":"confirmed_time"},'
            + N'"scheduling":{'
                + N'"slotIntervalMinutes":60,'
                + N'"bufferBetweenAppointmentsMinutes":0,'
                + N'"requireEmployee":true,'
                + N'"employeeStrategy":"least_versatile",'
                + N'"schedule":{'
                    + N'"monday":[{"open":"09:00","close":"18:00"}],'
                    + N'"tuesday":[{"open":"09:00","close":"18:00"}],'
                    + N'"wednesday":[{"open":"09:00","close":"18:00"}],'
                    + N'"thursday":[{"open":"09:00","close":"18:00"}],'
                    + N'"friday":[{"open":"09:00","close":"18:00"}],'
                    + N'"saturday":[{"open":"09:00","close":"14:00"}]'
                + N'}'
            + N'},'
            + N'"requiredVariables":["service","desired_date","desired_time"]'
        + N'}},'
        + N'{"id":"act_resolve_pricing","label":"Resolver precios","slot":1,"config":{'
            + N'"action_type":"resolve_pricing",'
            + N'"input_mapping":{"item":"{{variables.service}}","selected_add_ons":"{{variables.selected_add_ons}}"},'
            + N'"output_mapping":{"service_price":"service_price","addons_detail":"addons_detail","total_price":"total_price","total_price_invariant":"total_price_invariant"},'
            + N'"requiredVariables":["service","selected_add_ons","customer_name","email","baby_name","baby_age"],'
            + N'"requiredFlags":["availability_confirmed"],'
            + N'"onSuccessSetFlags":{"confirmation_summary_presented":true}'
        + N'}}'
    + N'],'
    + N'"knowledge":[{"id":"ks_reservas_catalogo","label":"Catalogo","slot":2,"config":{"knowledgeSourceId":"65D838C3-0FC2-41B9-822C-EDD479E545F8"}}]'
+ N'}},'

-- ---- Accept booking (type 4, passthrough) -----------------------------------
+ N'{"id":"accept_booking","type":4,"label":"Confirmar reserva","config":{'
    + N'"setFlags":{'
        + N'"confirmation_summary_presented":false,'
        + N'"__agentStep:agent_reservas:0":false,'
        + N'"__agentStep:agent_reservas:1":false'
    + N'},'
    + N'"responseMode":"template",'
    + N'"waitForUser":false,'
    + N'"instructions":"",'
    + N'"_ui":{"x":1100,"y":400}'
+ N'}},'

-- ---- Agent Reagendar (type 10, cluster con subNodes) ------------------------
+ N'{"id":"agent_reagendar","type":10,"label":"Agente Reagendar","handlesIntent":"user_wants_to_reschedule","config":{'
    + N'"collect":{'
        + N'"fields":["desired_date","desired_time"],'
        + N'"instructions":"El cliente quiere reagendar su cita. Pregunta la nueva fecha y hora deseada."'
    + N'},'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"Informa al cliente que su cita fue reagendada exitosamente.",'
    + N'"completionPort":"completed",'
    + N'"_ui":{"x":700,"y":700}'
+ N'},"subNodes":{'
    + N'"extract":{"id":"ext_reagendar","label":"Extraccion Reagendar","slot":0,"config":{"fields":["desired_date","desired_time"]}},'
    + N'"actions":['
        + N'{"id":"act_setup_reschedule","label":"Setup Reschedule","slot":1,"config":{'
            + N'"action_type":"setup_reschedule",'
            + N'"output_mapping":{"reservation_id":"original_reservation_id","service":"original_service","flag:is_rescheduling":"success"},'
            + N'"requiredVariables":[]'
        + N'}},'
        + N'{"id":"act_reschedule","label":"Reagendar","slot":1,"config":{'
            + N'"action_type":"reschedule",'
            + N'"input_mapping":{"reservation_id":"{{variables.reservation_id}}","date":"{{variables.desired_date}}","time":"{{variables.desired_time}}"},'
            + N'"output_mapping":{"flag:reservation_created":"success"},'
            + N'"requiredVariables":["desired_date","desired_time"],'
            + N'"requiredFlags":["is_rescheduling"],'
            + N'"onSuccessTemplate":"Tu cita ha sido reagendada exitosamente para el {{variables.desired_date}} a las {{variables.desired_time}}.",'
            + N'"onFailureTemplate":"No pudimos reagendar tu cita. Quieres hablar con una asesora?"'
        + N'}}'
    + N']'
+ N'}},'

-- ---- Generar link de pago (type 2) ------------------------------------------
+ N'{"id":"generate_payment_link","type":2,"label":"Generar link de pago","config":{'
    + N'"action_type":"generate_payment_link",'
    + N'"input_mapping":{"item":"{{variables.service}}","attributes":"{{variables_group:business}}"},'
    + N'"output_mapping":{"payment_link_url":"link_url","payment_reference_id":"reference_id"},'
    + N'"payment":{"requiresAnticipo":false,"anticipoPercentage":50,"currency":"COP","expirationMinutes":1440},'
    + N'"_ui":{"x":1400,"y":400}'
+ N'}},'

-- ---- Esperar pago (type 5) --------------------------------------------------
+ N'{"id":"wait_payment","type":5,"label":"Esperar pago","config":{'
    + N'"event_type":"payment_confirmed",'
    + N'"waitingMessage":"Para confirmar tu reserva, realiza el anticipo del 50% usando el siguiente link de pago seguro:\r\n\r\n> {{variables.payment_link_url}}\r\n\r\nUna vez confirmado el pago, tu reserva quedara asegurada automaticamente.\r\nSi ya realizaste el pago, escribenos <<ya pague>> para verificarlo.\r\nSi el link no funciona o expiro, escribe <<nuevo link>> y te enviamos uno actualizado.",'
    + N'"localIntentions":['
        + N'{"key":"user_says_paid","description":"El usuario afirma que ya realizo el pago","detectionExamples":["ya pague","listo el pago","ya transferi","hice el pago"],"behavior":{"action":"advance_port","targetPort":"user_claims_done"}},'
        + N'{"key":"user_wants_new_link","description":"El usuario pide un nuevo link de pago","detectionExamples":["otro link","no funciona el link","mandame otro link"],"behavior":{"action":"advance_port","targetPort":"new_link_requested"}}'
    + N'],'
    + N'"instructions":"ESTADO: ESPERANDO CONFIRMACION DE PAGO. El link ya fue enviado.\r\nREGLAS:\r\n- Si dice que ya pago -> <<Perfecto, el sistema verificara automaticamente.>>\r\n- Si el link expiro o pide otro -> indica que escriba <<nuevo link>>.\r\n- [X] PROHIBIDO afirmar que la reserva esta confirmada.\r\n- [X] PROHIBIDO mostrar ni inventar links de pago.",'
    + N'"_ui":{"x":1700,"y":400}'
+ N'}},'

-- ---- Verificar pago (type 2) ------------------------------------------------
+ N'{"id":"verify_payment","type":2,"label":"Verificar pago","config":{'
    + N'"action_type":"verify_payment",'
    + N'"input_mapping":{"reference_id":"{{variables.payment_reference_id}}"},'
    + N'"output_mapping":{"flag:payment_confirmed":"confirmed"},'
    + N'"_ui":{"x":2000,"y":260}'
+ N'}},'

-- ---- Pago no encontrado (type 4) --------------------------------------------
+ N'{"id":"payment_not_found","type":4,"label":"Pago no encontrado","config":{'
    + N'"responseMode":"template",'
    + N'"waitForUser":true,'
    + N'"instructions":"Aun no encontramos tu pago registrado.\r\n\r\nPuede tardar unos minutos en procesarse. Opciones:\r\n- Espera unos minutos y escribenos <<ya pague>>.\r\n- Usa el link si aun esta activo: {{variables.payment_link_url}}\r\n- Escribe <<nuevo link>> si necesitas uno actualizado.\r\n\r\nEstamos pendientes para confirmarte.",'
    + N'"_ui":{"x":2000,"y":540}'
+ N'}},'

-- ---- Crear reserva (type 2) -------------------------------------------------
+ N'{"id":"create_reservation","type":2,"label":"Crear reserva","config":{'
    + N'"action_type":"create_reservation",'
    + N'"input_mapping":{"item":"{{variables.service}}","date":"{{variables.desired_date}}","time":"{{variables.desired_time}}","customer_name":"{{variables.customer_name}}","customer_email":"{{variables.email}}","customer_phone":"{{variables.phone}}","attributes":"{{variables_group:business}}","selected_add_ons":"{{variables.selected_add_ons}}"},'
    + N'"output_mapping":{"reservation_id":"reservation_id","flag:reservation_created":"success"},'
    + N'"_ui":{"x":2300,"y":400}'
+ N'}},'

-- ---- Confirmacion exitosa (type 4) ------------------------------------------
+ N'{"id":"success_response","type":4,"label":"Confirmacion exitosa","config":{'
    + N'"responseMode":"template",'
    + N'"waitForUser":false,'
    + N'"instructions":"*Reserva confirmada!*\r\n\r\nNumero de reserva: #{{variables.reservation_id}}\r\nServicio: {{variables.service}}\r\nFecha: {{variables.desired_date}} a las {{variables.desired_time}}\r\nBebe: {{variables.baby_name}}\r\nCliente: {{variables.customer_name}}\r\nEmail: {{variables.email}}\r\n\r\nTe esperamos con mucho cariNo en Mimo''s Baby Spa!\r\nSi necesitas cambiar tu cita o tienes alguna pregunta, escribenos con gusto.",'
    + N'"_ui":{"x":2600,"y":400}'
+ N'}},'

-- ---- Pausar reserva (type 2) ------------------------------------------------
+ N'{"id":"hold_handler","type":2,"label":"Pausar reserva","config":{'
    + N'"action_type":"suspend",'
    + N'"input_mapping":{"reservation_id":"{{variables.reservation_id}}"},'
    + N'"onSuccessTemplate":"Tu reserva esta en pausa. Cuando quieras reagendar, escribenos y con gusto te ayudamos.",'
    + N'"onFailureTemplate":"No encontre una reserva activa para pausar. Quieres hablar con una asesora?",'
    + N'"_ui":{"x":300,"y":700}'
+ N'}},'

-- ---- Escalar (type 6) -------------------------------------------------------
+ N'{"id":"escalate","type":6,"label":"Escalar a humano","config":{'
    + N'"reason":"El cliente solicito atencion personalizada o el sistema no pudo completar el proceso automaticamente.",'
    + N'"contacts":[],'
    + N'"escalationMessage":"Te estoy conectando con un asesor. Un momento por favor.",'
    + N'"_ui":{"x":2600,"y":640}'
+ N'}},'

-- ---- Fin (type 7) -----------------------------------------------------------
+ N'{"id":"end","type":7,"label":"Fin del flujo","config":{"_ui":{"x":2860,"y":400}}}'

+ N']';

IF ISJSON(@NodesNew) = 0
BEGIN
    SELECT LEN(@NodesNew) AS NodeJsonLen,
           LEFT(@NodesNew, 4000) AS Part1,
           SUBSTRING(@NodesNew, 4001, 4000) AS Part2,
           SUBSTRING(@NodesNew, 8001, 4000) AS Part3;
    RAISERROR(N'041: nodes JSON invalido -- abortar.', 16, 1);
    RETURN;
END;

-- =============================================================================
-- ARISTAS - 23 aristas (simplificadas sin extract_modern ni collect_booking_data)
-- =============================================================================

DECLARE @EdgesNew NVARCHAR(MAX);
SET @EdgesNew = CAST(N'' AS NVARCHAR(MAX)) + N'['

-- start -> router (directo, sin extract_modern)
+ N'{"id":"e041_st_rt","sourceNodeId":"start","targetNodeId":"router"},'

-- router -> ramas
+ N'{"id":"e041_rt_pd","sourceNodeId":"router","targetNodeId":"create_reservation","portId":"payment_done"},'
+ N'{"id":"e041_rt_in","sourceNodeId":"router","targetNodeId":"agent_info","portId":"information"},'
+ N'{"id":"e041_rt_rs","sourceNodeId":"router","targetNodeId":"agent_reservas","portId":"reserva"},'
+ N'{"id":"e041_rt_cn","sourceNodeId":"router","targetNodeId":"agent_cancelar","portId":"cancel"},'
+ N'{"id":"e041_rt_re","sourceNodeId":"router","targetNodeId":"agent_reagendar","portId":"reschedule"},'
+ N'{"id":"e041_rt_hd","sourceNodeId":"router","targetNodeId":"hold_handler","portId":"hold"},'

-- agent_reservas -> accept_booking
+ N'{"id":"e041_ar_ab","sourceNodeId":"agent_reservas","targetNodeId":"accept_booking","portId":"completed"},'

-- accept_booking -> generate_payment_link
+ N'{"id":"e041_ab_gp","sourceNodeId":"accept_booking","targetNodeId":"generate_payment_link"},'

-- generate_payment_link
+ N'{"id":"e041_gp_wp","sourceNodeId":"generate_payment_link","targetNodeId":"wait_payment","portId":"success"},'
+ N'{"id":"e041_gp_cr","sourceNodeId":"generate_payment_link","targetNodeId":"create_reservation","portId":"not_required"},'
+ N'{"id":"e041_gp_es","sourceNodeId":"generate_payment_link","targetNodeId":"escalate","portId":"failure"},'

-- wait_payment
+ N'{"id":"e041_wp_cr","sourceNodeId":"wait_payment","targetNodeId":"create_reservation","portId":"received"},'
+ N'{"id":"e041_wp_vp","sourceNodeId":"wait_payment","targetNodeId":"verify_payment","portId":"user_claims_done"},'
+ N'{"id":"e041_wp_gp","sourceNodeId":"wait_payment","targetNodeId":"generate_payment_link","portId":"new_link_requested"},'

-- verify_payment
+ N'{"id":"e041_vp_cr","sourceNodeId":"verify_payment","targetNodeId":"create_reservation","portId":"success"},'
+ N'{"id":"e041_vp_pn","sourceNodeId":"verify_payment","targetNodeId":"payment_not_found","portId":"failure"},'

-- payment_not_found
+ N'{"id":"e041_pn_wp","sourceNodeId":"payment_not_found","targetNodeId":"wait_payment"},'

-- create_reservation
+ N'{"id":"e041_cr_sr","sourceNodeId":"create_reservation","targetNodeId":"success_response","portId":"success"},'
+ N'{"id":"e041_cr_es","sourceNodeId":"create_reservation","targetNodeId":"escalate","portId":"failure"},'

-- success_response
+ N'{"id":"e041_sr_en","sourceNodeId":"success_response","targetNodeId":"end"},'

-- agent_cancelar (regresa a router para nueva clasificación)
+ N'{"id":"e041_ac_rt","sourceNodeId":"agent_cancelar","targetNodeId":"router","portId":"completed"},'

-- agent_reagendar
+ N'{"id":"e041_ag_sr","sourceNodeId":"agent_reagendar","targetNodeId":"success_response","portId":"completed"},'
+ N'{"id":"e041_ag_es","sourceNodeId":"agent_reagendar","targetNodeId":"escalate","portId":"failure"},'

-- hold_handler
+ N'{"id":"e041_hh_en","sourceNodeId":"hold_handler","targetNodeId":"end","portId":"success"},'
+ N'{"id":"e041_hh_es","sourceNodeId":"hold_handler","targetNodeId":"escalate","portId":"failure"},'

-- escalate
+ N'{"id":"e041_es_en","sourceNodeId":"escalate","targetNodeId":"end"}'

+ N']';

IF ISJSON(@EdgesNew) = 0
BEGIN
    RAISERROR(N'041: edges JSON invalido -- abortar.', 16, 1);
    RETURN;
END;

-- =============================================================================
-- Construir @Out: preservar variables, sessionConfig, engineSettings,
-- extractionInstructions del JSON actual + reemplazar nodos/aristas +
-- agregar routingIntents + limpiar intentionSchema de routing behaviors
-- =============================================================================

DECLARE @Out NVARCHAR(MAX) = @Json;
SET @Out = JSON_MODIFY(@Out, N'$.nodes', JSON_QUERY(@NodesNew));
SET @Out = JSON_MODIFY(@Out, N'$.edges', JSON_QUERY(@EdgesNew));
SET @Out = JSON_MODIFY(@Out, N'$.routingIntents', JSON_QUERY(@RoutingIntents));

-- Limpiar intentionSchema: quitar goto_node behaviors que ahora maneja el Router
-- user_wants_to_cancel: ya no necesita goto_node
DECLARE @CancelIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_wants_to_cancel'
);
IF @CancelIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@CancelIdx AS NVARCHAR(12)) + N'].behavior',
        JSON_QUERY(N'{"action":"none"}'));

-- user_wants_to_reschedule: ya no necesita goto_node
DECLARE @ReschedIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_wants_to_reschedule'
);
IF @ReschedIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@ReschedIdx AS NVARCHAR(12)) + N'].behavior',
        JSON_QUERY(N'{"action":"none"}'));

-- user_requested_availability: ya no necesita goto_node (integrado en agent_reservas)
DECLARE @AvailIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_requested_availability'
);
IF @AvailIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@AvailIdx AS NVARCHAR(12)) + N'].behavior',
        JSON_QUERY(N'{"action":"none"}'));

-- user_wants_to_hold: ya no necesita goto_node
DECLARE @HoldIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_wants_to_hold'
);
IF @HoldIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@HoldIdx AS NVARCHAR(12)) + N'].behavior',
        JSON_QUERY(N'{"action":"none"}'));

-- =============================================================================
-- Migrar FlowExecutionStates activos: remap CurrentNodeId
--
-- Cubre TODOS los estados posibles:
--   - DB original (009): detect_intent, collect_service, etc.
--   - DB intermedia (037-040): extract_modern, main_router, agent_reserva, etc.
--
-- Nodos válidos en la nueva arquitectura (16):
--   start, router, agent_info, agent_cancelar, agent_reservas, agent_reagendar,
--   accept_booking, generate_payment_link, wait_payment, verify_payment,
--   payment_not_found, create_reservation, success_response, hold_handler,
--   escalate, end
-- =============================================================================

-- Tabla temporal con el mapeo completo de nodos viejos → nuevos
DECLARE @NodeRemap TABLE (OldNodeId NVARCHAR(100), NewNodeId NVARCHAR(100));
INSERT INTO @NodeRemap (OldNodeId, NewNodeId) VALUES
    -- Desde estado 009 (original)
    (N'detect_intent',          N'router'),
    (N'info_response',          N'agent_info'),
    (N'collect_service',        N'agent_reservas'),
    (N'offer_addons',           N'agent_reservas'),
    (N'collect_date',           N'agent_reservas'),
    (N'check_availability',     N'agent_reservas'),
    (N'show_alternatives',      N'agent_reservas'),
    (N'collect_identity',       N'agent_reservas'),
    (N'show_confirmation',      N'agent_reservas'),
    (N'detect_confirmation',    N'agent_reservas'),
    (N'cancel_response',        N'agent_cancelar'),
    (N'reschedule_reservation', N'agent_reagendar'),
    (N'reschedule_setup',       N'agent_reagendar'),
    -- Desde estado intermedio (037-040)
    (N'extract_flow_entry',     N'router'),
    (N'extract_modern',         N'router'),
    (N'main_router',            N'router'),
    (N'collect_booking_data',   N'agent_reservas'),
    (N'agent_reserva',          N'agent_reservas');

IF @DryRun = 1
BEGIN
    PRINT N'041 DRY RUN OK. FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
    PRINT N'Nodos: 16. Aristas: 27.';
    PRINT N'routingIntents: 5 intenciones.';
    PRINT N'Router con clasificacion integrada (3 fases).';
    PRINT N'Agent nodes con subNodes (extract, actions, knowledge).';
    PRINT N'';
    PRINT N'FlowExecutionStates a migrar:';

    SELECT fes.FlowExecutionStateId,
           fes.CurrentNodeId AS OldNodeId,
           COALESCE(r.NewNodeId, fes.CurrentNodeId) AS NewNodeId,
           CASE WHEN r.NewNodeId IS NOT NULL THEN N'REMAP' ELSE N'OK' END AS Status
    FROM dbo.FlowExecutionStates fes
    LEFT JOIN @NodeRemap r ON r.OldNodeId = fes.CurrentNodeId
    WHERE fes.FlowDefinitionId = @FlowDefinitionId;

    PRINT N'Pon @DryRun = 0 para aplicar.';
    SELECT @Out AS DefinitionJson;
    RETURN;
END;

BEGIN TRANSACTION;

UPDATE dbo.FlowDefinitions
   SET DefinitionJson = @Out,
       UpdatedAt      = GETUTCDATE()
WHERE FlowDefinitionId = @FlowDefinitionId;

UPDATE fes SET
    CurrentNodeId = r.NewNodeId
FROM dbo.FlowExecutionStates fes
INNER JOIN @NodeRemap r ON r.OldNodeId = fes.CurrentNodeId
WHERE fes.FlowDefinitionId = @FlowDefinitionId;

-- Limpiar flags internas de agent steps de nombres viejos
UPDATE fes SET
    FlagsJson = (
        SELECT N'{' + STRING_AGG(N'"' + j.[key] + N'":' + j.value, N',') + N'}'
        FROM OPENJSON(fes.FlagsJson) j
        WHERE j.[key] NOT LIKE N'__agentStep:agent_reserva:%'
          AND j.[key] NOT LIKE N'__agentStep:collect_booking_data:%'
    )
FROM dbo.FlowExecutionStates fes
WHERE fes.FlowDefinitionId = @FlowDefinitionId
  AND (fes.FlagsJson LIKE N'%__agentStep:agent_reserva:%'
    OR fes.FlagsJson LIKE N'%__agentStep:collect_booking_data:%');

COMMIT TRANSACTION;

PRINT N'041: Aplicado. FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
PRINT N'Nodos migrados. Cluster node architecture activa.';
GO
