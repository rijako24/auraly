-- =============================================================================
-- 040: Consolidar flujo en nodos Agent compuestos (18 nodos, 28 aristas)
--
-- Cambios principales:
--   1. agent_reserva: absorbe show_confirmation (el agente presenta resumen
--      via responseMode tras completar pipeline).
--   2. Fusiona reschedule_setup + reschedule_reservation en agent_reagendar
--      (type 10) con pipeline: [setup_reschedule, reschedule].
--   3. info_response se convierte en agent_info (type 10) sin pipeline,
--      responseMode = llm.
--   4. cancel_response se convierte en agent_cancelar (type 10) sin pipeline,
--      resetea variables/flags y responde con LLM.
--   5. main_router ahora enruta tambien a cancel, reschedule, hold para que
--      el diagrama muestre todas las conexiones.
--
-- Nodos (18):
--   start, extract_modern, main_router,
--   agent_info, agent_cancelar, collect_booking_data, agent_reserva, agent_reagendar,
--   accept_booking,
--   generate_payment_link, wait_payment, verify_payment, payment_not_found,
--   create_reservation, success_response,
--   hold_handler, escalate, end
--
-- @DryRun = 1 -> SELECT sin actualizar BD.
-- @DryRun = 0 -> UPDATE FlowDefinitions.
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
    RAISERROR(N'040: No se encontro FlowDefinitionId.', 16, 1);
    RETURN;
END;

DECLARE @Json NVARCHAR(MAX);
SELECT @Json = fd.DefinitionJson
FROM dbo.FlowDefinitions fd
WHERE fd.FlowDefinitionId = @FlowDefinitionId;

IF @Json IS NULL OR ISJSON(@Json) = 0
BEGIN
    RAISERROR(N'040: DefinitionJson invalido o vacio.', 16, 1);
    RETURN;
END;

-- =============================================================================
-- NODOS - 18 nodos con layout organizado izquierda-derecha
--
--  y=40:   [agent_cancelar]                    [agent_info]
--  y=400:  [start] > [extract] > [router] > [collect_data] > [agent_reserva] > [accept] > [gen_link] > [wait] > [verify] > [create] > [success] > [end]
--                                                                                               [pay_nf]
--  y=700:            [hold]      [agent_reagendar]                                                             [escalate]
-- =============================================================================

DECLARE @NodesNew NVARCHAR(MAX);
SET @NodesNew = CAST(N'' AS NVARCHAR(MAX)) + N'['

-- ---- Control -----------------------------------------------------------------
+ N'{"id":"start","type":0,"label":"Inicio","config":{"_ui":{"x":50,"y":400,"groupId":"entry","groupLabel":"Inicio y extraccion"}}},'
+ N'{"id":"extract_modern","type":8,"label":"Extraccion (IA)","config":{"catalogKey":"extract","_ui":{"x":300,"y":400,"groupId":"entry","groupLabel":"Inicio y extraccion"}}},'

-- ---- Router principal (type 9) -----------------------------------------------
-- Rutas: payment_done, information, cancel, reschedule, hold, default=reserva.
-- cancel/reschedule/hold son respaldo visual; goto_node en intentionSchema
-- salta antes de alcanzar el router.
+ N'{"id":"main_router","type":9,"label":"Enrutar","config":{'
    + N'"routes":['
        + N'{"when":{"type":"flag_true","flag":"payment_confirmed"},"port":"payment_done"},'
        + N'{"when":"is_information_query","port":"information"},'
        + N'{"when":"user_wants_to_cancel","port":"cancel"},'
        + N'{"when":"user_wants_to_reschedule","port":"reschedule"},'
        + N'{"when":"user_wants_to_hold","port":"hold"}'
    + N'],'
    + N'"defaultPort":"reserva",'
    + N'"_ui":{"x":560,"y":400,"groupId":"router","groupLabel":"Enrutador principal"}'
+ N'}},'

-- ---- Agent Info (type 10, sin pipeline) --------------------------------------
+ N'{"id":"agent_info","type":10,"label":"Agente Info","config":{'
    + N'"catalogKey":"agent",'
    + N'"actionPipeline":[],'
    + N'"collect":{'
        + N'"fields":[],'
        + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8","BBD27A73-02D7-4F8D-A80A-2928F3A8BC03"],'
        + N'"instructions":"El usuario pregunta sobre servicios, precios, beneficios o planes del spa.\r\n\r\nSi NO pregunta por un plan concreto: presenta CATEGORIAS (Baby Spa, Estimulacion Temprana, Materno Spa, etc.).\r\nSi pregunta por UN plan especifico: detalla SOLO ese plan.\r\nSi explora una categoria: presenta planes con precios exactos. Mayor valor primero.\r\nNO inventes precios ni informacion."'
    + N'},'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"Responde la consulta informativa del usuario usando el catalogo disponible.",'
    + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8","BBD27A73-02D7-4F8D-A80A-2928F3A8BC03"],'
    + N'"completionPort":"completed",'
    + N'"_ui":{"x":960,"y":40,"groupId":"information","groupLabel":"Informacion","groupCollapse":false}'
+ N'}},'

-- ---- Agent Cancelar (type 10, sin pipeline, resetea todo) --------------------
+ N'{"id":"agent_cancelar","type":10,"label":"Agente Cancelar","config":{'
    + N'"catalogKey":"agent",'
    + N'"actionPipeline":[],'
    + N'"collect":{"fields":[],"instructions":"El cliente quiere cancelar."},'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"El cliente decidio no continuar con el proceso de reserva.\r\n- Acepta sin presionar ni insistir.\r\n- Agradece su tiempo con calidez.\r\n- Ofrece comenzar de nuevo cuando lo desee.",'
    + N'"completionPort":"completed",'
    + N'"_ui":{"x":220,"y":40,"groupId":"exceptions","groupLabel":"Excepciones","groupCollapse":true}'
+ N'}},'

-- ---- Agent Reservas (type 10, pipeline: check_avail + resolve_pricing) --------
-- ---- Recolección de data previa a reserva (type 1) -----------------------------
+ N'{"id":"collect_booking_data","type":1,"label":"Recoleccion de data","config":{'
    + N'"fields":["service","desired_date","desired_time","customer_name","email","baby_name","baby_age"],'
    + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8"],'
    + N'"instructions":"Antes de reservar, recolecta de forma conversacional: servicio, fecha/hora deseada y datos del cliente/bebe. Si falta algo, pide solo lo faltante; cuando todo este completo, avanza.",'
    + N'"_ui":{"x":820,"y":400,"groupId":"reservation","groupLabel":"Agente Reservas","groupCollapse":true}'
+ N'}},'

-- ---- Agent Reservas (type 10, pipeline: check_avail + resolve_pricing) --------
+ N'{"id":"agent_reserva","type":10,"label":"Agente Reservas","config":{'
    + N'"catalogKey":"agent",'
    + N'"collect":{'
        + N'"fields":["service","desired_date","desired_time","selected_add_ons","customer_name","email","baby_name","baby_age"],'
        + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8"],'
        + N'"instructions":"El sistema indica si es el primer mensaje en CONTEXTO DE SESION.\r\n\r\n> PRIMER MENSAJE (Primera interaccion: Si):\r\n  - Saluda con calidez y presentate.\r\n  - Presenta SIEMPRE las categorias: Planes Baby Spa, Talleres Estimulacion Temprana, Materno Spa, Dulce Espera, Programa Iniciacion al Jardin.\r\n  - Si conoces la edad del bebe, menciona que categorias se adaptan mejor.\r\n  - NUNCA digas <<algun otro servicio>> - nombra las opciones explicitamente.\r\n\r\n> CLIENTE RECURRENTE (Cliente recurrente: Si):\r\n  - NO te presentes de nuevo. Saluda reconociendo que regresa.\r\n  - Si solo saluda -> pregunta en que puedes ayudarle.\r\n\r\n> RECOLECCION DE DATOS (conversacion en curso):\r\n  - Pide datos UNO A LA VEZ, de forma conversacional y calida.\r\n  - Orden natural: 1) servicio -> 2) fecha y hora -> 3) extras Cumplemes (SOLO despues de confirmar disponibilidad) -> 4) nombre, email, bebe.\r\n  - Si la disponibilidad fue confirmada en este turno: ofrece Cumplemes naturalmente (sencilla $35.000 o bouquet $55.000).\r\n  - Cuando haya horarios disponibles (no exactos): presentalos y pide que elija.\r\n  - NO pidas datos personales antes de confirmar disponibilidad y extras.\r\n  - NO inventes precios ni informacion - usa el catalogo disponible."'
    + N'},'
    + N'"actionPipeline":['
        + N'{'
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
        + N'},'
        + N'{'
            + N'"action_type":"resolve_pricing",'
            + N'"input_mapping":{"item":"{{variables.service}}","selected_add_ons":"{{variables.selected_add_ons}}"},'
            + N'"output_mapping":{"service_price":"service_price","addons_detail":"addons_detail","total_price":"total_price","total_price_invariant":"total_price_invariant"},'
            + N'"requiredVariables":["service","selected_add_ons","customer_name","email","baby_name","baby_age"],'
            + N'"requiredFlags":["availability_confirmed"],'
            + N'"onSuccessSetFlags":{"confirmation_summary_presented":true}'
        + N'}'
    + N'],'
    + N'"responseMode":"llm",'
    + N'"instructions":"ESTADO: TODOS LOS DATOS RECOPILADOS Y PRECIO CALCULADO.\r\nPresenta el resumen completo de la reserva y pide confirmacion al cliente.\r\n\r\n*Resumen de tu reserva:*\r\n{{collected_data}}\r\n\r\nConfirmas todos estos datos para proceder con el pago?\r\n(Responde <<Si>> para confirmar o cuentame que deseas cambiar.)",'
    + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8"],'
    + N'"completionBehavior":"respond",'
    + N'"completionPort":"completed",'
    + N'"waitForUser":true,'
    + N'"_ui":{"x":1160,"y":220,"groupId":"reservation","groupLabel":"Agente Reservas","groupCollapse":true,"subnodes":[{"id":"collect_booking_data","label":"Recoleccion de data","type":"collect"},{"id":"check_availability","label":"Verificar disponibilidad","type":"action","group":"reservas"},{"id":"resolve_pricing","label":"Resolver precios","type":"action","group":"reservas"},{"id":"accept_booking","label":"Confirmar reserva","type":"response","group":"reservas"},{"id":"agent_pagos","label":"Agente Pagos","type":"composite","group":"pagos"},{"id":"create_reservation","label":"Crear reserva","type":"action","group":"reservas"},{"id":"hold_handler","label":"Pausar reserva","type":"action","group":"reservas"}]}'
+ N'}},'

-- ---- Aceptar reserva (type 4, passthrough) -----------------------------------
+ N'{"id":"accept_booking","type":4,"label":"Confirmar reserva","config":{'
    + N'"setFlags":{'
        + N'"confirmation_summary_presented":false,'
        + N'"__agentStep:agent_reserva:0":false,'
        + N'"__agentStep:agent_reserva:1":false'
    + N'},'
    + N'"responseMode":"template",'
    + N'"waitForUser":false,'
    + N'"instructions":"",'
    + N'"_ui":{"x":1560,"y":400,"groupId":"reservation","groupLabel":"Agente Reservas","groupCollapse":true}'
+ N'}},'

-- ---- Agent Reagendar (type 10, pipeline: setup_reschedule + reschedule) -------
+ N'{"id":"agent_reagendar","type":10,"label":"Agente Reagendar","config":{'
    + N'"catalogKey":"agent",'
    + N'"collect":{'
        + N'"fields":["desired_date","desired_time"],'
        + N'"instructions":"El cliente quiere reagendar su cita. Pregunta la nueva fecha y hora deseada."'
    + N'},'
    + N'"actionPipeline":['
        + N'{'
            + N'"action_type":"setup_reschedule",'
            + N'"output_mapping":{"reservation_id":"original_reservation_id","service":"original_service","flag:is_rescheduling":"success"},'
            + N'"requiredVariables":[]'
        + N'},'
        + N'{'
            + N'"action_type":"reschedule",'
            + N'"input_mapping":{"reservation_id":"{{variables.reservation_id}}","date":"{{variables.desired_date}}","time":"{{variables.desired_time}}"},'
            + N'"output_mapping":{"flag:reservation_created":"success"},'
            + N'"requiredVariables":["desired_date","desired_time"],'
            + N'"requiredFlags":["is_rescheduling"],'
            + N'"onSuccessTemplate":"Tu cita ha sido reagendada exitosamente para el {{variables.desired_date}} a las {{variables.desired_time}}.",'
            + N'"onFailureTemplate":"No pudimos reagendar tu cita. Quieres hablar con una asesora?"'
        + N'}'
    + N'],'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"Informa al cliente que su cita fue reagendada exitosamente.",'
    + N'"completionPort":"completed",'
    + N'"_ui":{"x":960,"y":700,"groupId":"exceptions","groupLabel":"Excepciones","groupCollapse":true}'
+ N'}},'

-- ---- Generar link de pago (type 2) ------------------------------------------
+ N'{"id":"generate_payment_link","type":2,"label":"Generar link de pago","config":{'
    + N'"action_type":"generate_payment_link",'
    + N'"input_mapping":{"item":"{{variables.service}}","attributes":"{{variables_group:business}}"},'
    + N'"output_mapping":{"payment_link_url":"link_url","payment_reference_id":"reference_id"},'
    + N'"payment":{"requiresAnticipo":false,"anticipoPercentage":50,"currency":"COP","expirationMinutes":1440},'
    + N'"_ui":{"x":1820,"y":400,"groupId":"payment","groupLabel":"Agente Pagos","groupCollapse":true}'
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
    + N'"_ui":{"x":2100,"y":400,"groupId":"payment","groupLabel":"Agente Pagos","groupCollapse":true}'
+ N'}},'

-- ---- Verificar pago (type 2) ------------------------------------------------
+ N'{"id":"verify_payment","type":2,"label":"Verificar pago","config":{'
    + N'"action_type":"verify_payment",'
    + N'"input_mapping":{"reference_id":"{{variables.payment_reference_id}}"},'
    + N'"output_mapping":{"flag:payment_confirmed":"confirmed"},'
    + N'"_ui":{"x":2400,"y":260,"groupId":"payment","groupLabel":"Agente Pagos","groupCollapse":true}'
+ N'}},'

-- ---- Pago no encontrado (type 4) --------------------------------------------
+ N'{"id":"payment_not_found","type":4,"label":"Pago no encontrado","config":{'
    + N'"responseMode":"template",'
    + N'"waitForUser":true,'
    + N'"instructions":"Aun no encontramos tu pago registrado.\r\n\r\nPuede tardar unos minutos en procesarse. Opciones:\r\n- Espera unos minutos y escribenos <<ya pague>>.\r\n- Usa el link si aun esta activo: {{variables.payment_link_url}}\r\n- Escribe <<nuevo link>> si necesitas uno actualizado.\r\n\r\nEstamos pendientes para confirmarte.",'
    + N'"_ui":{"x":2400,"y":540,"groupId":"payment","groupLabel":"Agente Pagos","groupCollapse":true}'
+ N'}},'

-- ---- Crear reserva (type 2) -------------------------------------------------
+ N'{"id":"create_reservation","type":2,"label":"Crear reserva","config":{'
    + N'"action_type":"create_reservation",'
    + N'"input_mapping":{"item":"{{variables.service}}","date":"{{variables.desired_date}}","time":"{{variables.desired_time}}","customer_name":"{{variables.customer_name}}","customer_email":"{{variables.email}}","customer_phone":"{{variables.phone}}","attributes":"{{variables_group:business}}","selected_add_ons":"{{variables.selected_add_ons}}"},'
    + N'"output_mapping":{"reservation_id":"reservation_id","flag:reservation_created":"success"},'
    + N'"_ui":{"x":2700,"y":400,"groupId":"reservation","groupLabel":"Agente Reservas","groupCollapse":true}'
+ N'}},'

-- ---- Confirmacion exitosa (type 4) ------------------------------------------
+ N'{"id":"success_response","type":4,"label":"Confirmacion exitosa","config":{'
    + N'"responseMode":"template",'
    + N'"waitForUser":false,'
    + N'"instructions":"*Reserva confirmada!*\r\n\r\nNumero de reserva: #{{variables.reservation_id}}\r\nServicio: {{variables.service}}\r\nFecha: {{variables.desired_date}} a las {{variables.desired_time}}\r\nBebe: {{variables.baby_name}}\r\nCliente: {{variables.customer_name}}\r\nEmail: {{variables.email}}\r\n\r\nTe esperamos con mucho cariNo en Mimo''s Baby Spa!\r\nSi necesitas cambiar tu cita o tienes alguna pregunta, escribenos con gusto.",'
    + N'"_ui":{"x":3000,"y":400,"groupId":"closure","groupLabel":"Cierre"}'
+ N'}},'

-- ---- Pausar reserva (type 2) ------------------------------------------------
+ N'{"id":"hold_handler","type":2,"label":"Pausar reserva","config":{'
    + N'"action_type":"suspend",'
    + N'"input_mapping":{"reservation_id":"{{variables.reservation_id}}"},'
    + N'"onSuccessTemplate":"Tu reserva esta en pausa. Cuando quieras reagendar, escribenos y con gusto te ayudamos.",'
    + N'"onFailureTemplate":"No encontre una reserva activa para pausar. Quieres hablar con una asesora?",'
    + N'"_ui":{"x":560,"y":700,"groupId":"reservation","groupLabel":"Agente Reservas","groupCollapse":true}'
+ N'}},'

-- ---- Escalar (type 6) -------------------------------------------------------
+ N'{"id":"escalate","type":6,"label":"Escalar a humano","config":{'
    + N'"reason":"El cliente solicito atencion personalizada o el sistema no pudo completar el proceso automaticamente.",'
    + N'"contacts":[],'
    + N'"escalationMessage":"Te estoy conectando con un asesor. Un momento por favor.",'
    + N'"_ui":{"x":3000,"y":640,"groupId":"exceptions","groupLabel":"Excepciones","groupCollapse":true}'
+ N'}},'

-- ---- Fin (type 7) -----------------------------------------------------------
+ N'{"id":"end","type":7,"label":"Fin del flujo","config":{"_ui":{"x":3260,"y":400,"groupId":"closure","groupLabel":"Cierre"}}}'

+ N']';

IF ISJSON(@NodesNew) = 0
BEGIN
    SELECT LEN(@NodesNew) AS NodeJsonLen,
           LEFT(@NodesNew, 4000) AS Part1,
           SUBSTRING(@NodesNew, 4001, 4000) AS Part2,
           SUBSTRING(@NodesNew, 8001, 4000) AS Part3;
    RAISERROR(N'040: nodes JSON invalido -- abortar.', 16, 1);
    RETURN;
END;

-- =============================================================================
-- ARISTAS - 28 aristas
--
-- Incluye rutas visuales desde el router a nodos de intencion (cancel,
-- reschedule, hold) y edges faltantes de hold_handler.
-- =============================================================================

DECLARE @EdgesNew NVARCHAR(MAX);
SET @EdgesNew = CAST(N'' AS NVARCHAR(MAX)) + N'['

-- Troncal: start -> extract -> router
+ N'{"id":"e040_st_ex","sourceNodeId":"start","targetNodeId":"extract_modern"},'
+ N'{"id":"e040_ex_mr","sourceNodeId":"extract_modern","targetNodeId":"main_router"},'

-- main_router -> ramas
+ N'{"id":"e040_mr_pd","sourceNodeId":"main_router","targetNodeId":"create_reservation","portId":"payment_done"},'
+ N'{"id":"e040_mr_in","sourceNodeId":"main_router","targetNodeId":"agent_info","portId":"information"},'
+ N'{"id":"e040_mr_rs","sourceNodeId":"main_router","targetNodeId":"collect_booking_data","portId":"reserva"},'
+ N'{"id":"e040_mr_cn","sourceNodeId":"main_router","targetNodeId":"agent_cancelar","portId":"cancel"},'
+ N'{"id":"e040_mr_re","sourceNodeId":"main_router","targetNodeId":"agent_reagendar","portId":"reschedule"},'
+ N'{"id":"e040_mr_hd","sourceNodeId":"main_router","targetNodeId":"hold_handler","portId":"hold"},'

-- collect_booking_data -> agent_reserva
+ N'{"id":"e040_cd_ar","sourceNodeId":"collect_booking_data","targetNodeId":"agent_reserva"},'

-- accept_booking (destino de goto_node user_confirmed_booking)
+ N'{"id":"e040_ab_gp","sourceNodeId":"accept_booking","targetNodeId":"generate_payment_link"},'

-- generate_payment_link
+ N'{"id":"e040_gp_wp","sourceNodeId":"generate_payment_link","targetNodeId":"wait_payment","portId":"success"},'
+ N'{"id":"e040_gp_cr","sourceNodeId":"generate_payment_link","targetNodeId":"create_reservation","portId":"not_required"},'
+ N'{"id":"e040_gp_es","sourceNodeId":"generate_payment_link","targetNodeId":"escalate","portId":"failure"},'

-- wait_payment
+ N'{"id":"e040_wp_cr","sourceNodeId":"wait_payment","targetNodeId":"create_reservation","portId":"received"},'
+ N'{"id":"e040_wp_vp","sourceNodeId":"wait_payment","targetNodeId":"verify_payment","portId":"user_claims_done"},'
+ N'{"id":"e040_wp_gp","sourceNodeId":"wait_payment","targetNodeId":"generate_payment_link","portId":"new_link_requested"},'

-- verify_payment
+ N'{"id":"e040_vp_cr","sourceNodeId":"verify_payment","targetNodeId":"create_reservation","portId":"success"},'
+ N'{"id":"e040_vp_pn","sourceNodeId":"verify_payment","targetNodeId":"payment_not_found","portId":"failure"},'

-- payment_not_found
+ N'{"id":"e040_pn_wp","sourceNodeId":"payment_not_found","targetNodeId":"wait_payment"},'

-- create_reservation
+ N'{"id":"e040_cr_sr","sourceNodeId":"create_reservation","targetNodeId":"success_response","portId":"success"},'
+ N'{"id":"e040_cr_es","sourceNodeId":"create_reservation","targetNodeId":"escalate","portId":"failure"},'

-- success_response
+ N'{"id":"e040_sr_en","sourceNodeId":"success_response","targetNodeId":"end"},'

-- agent_cancelar (reset + regresa a start)
+ N'{"id":"e040_ac_st","sourceNodeId":"agent_cancelar","targetNodeId":"start","portId":"completed"},'

-- agent_reagendar
+ N'{"id":"e040_ar_sr","sourceNodeId":"agent_reagendar","targetNodeId":"success_response","portId":"completed"},'
+ N'{"id":"e040_ar_es","sourceNodeId":"agent_reagendar","targetNodeId":"escalate","portId":"failure"},'

-- hold_handler
+ N'{"id":"e040_hh_en","sourceNodeId":"hold_handler","targetNodeId":"end","portId":"success"},'
+ N'{"id":"e040_hh_es","sourceNodeId":"hold_handler","targetNodeId":"escalate","portId":"failure"},'

-- escalate
+ N'{"id":"e040_es_en","sourceNodeId":"escalate","targetNodeId":"end"}'

+ N']';

IF ISJSON(@EdgesNew) = 0
BEGIN
    RAISERROR(N'040: edges JSON invalido -- abortar.', 16, 1);
    RETURN;
END;

-- =============================================================================
-- Construir @Out: preservar variables, intentionSchema, sessionConfig,
-- engineSettings, extractionInstructions del JSON actual + reemplazar nodos/aristas
-- =============================================================================

DECLARE @Out NVARCHAR(MAX) = @Json;
SET @Out = JSON_MODIFY(@Out, N'$.nodes', JSON_QUERY(@NodesNew));
SET @Out = JSON_MODIFY(@Out, N'$.edges', JSON_QUERY(@EdgesNew));

-- =============================================================================
-- Actualizar intentionSchema: redirigir nodos eliminados
-- =============================================================================

-- user_wants_to_cancel -> agent_cancelar (era cancel_response)
DECLARE @CancelIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_wants_to_cancel'
);
IF @CancelIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@CancelIdx AS NVARCHAR(12)) + N'].behavior.targetNodeId',
        N'agent_cancelar');

-- user_wants_to_reschedule -> agent_reagendar (era reschedule_setup)
DECLARE @ReschedIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_wants_to_reschedule'
);
IF @ReschedIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@ReschedIdx AS NVARCHAR(12)) + N'].behavior.targetNodeId',
        N'agent_reagendar');

-- user_requested_availability -> collect_booking_data (entrada ordenada por pre-etapa de datos)
DECLARE @AvailIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_requested_availability'
);
IF @AvailIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@AvailIdx AS NVARCHAR(12)) + N'].behavior.targetNodeId',
        N'collect_booking_data');

-- =============================================================================
-- Dry run o aplicar
-- =============================================================================

IF @DryRun = 1
BEGIN
    PRINT N'040 DRY RUN OK. FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
    PRINT N'Nodos: 18. Aristas: 28.';
    PRINT N'Router con rutas: payment_done, information, cancel, reschedule, hold, default=reserva.';
    PRINT N'Secuencia principal: ... -> main_router -> collect_booking_data -> agent_reserva -> ...';
    PRINT N'Pon @DryRun = 0 para aplicar.';
    SELECT @Out AS DefinitionJson;
    RETURN;
END;

UPDATE dbo.FlowDefinitions
   SET DefinitionJson = @Out,
       UpdatedAt      = GETUTCDATE()
WHERE FlowDefinitionId = @FlowDefinitionId;

PRINT N'040: Aplicado. FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
GO
