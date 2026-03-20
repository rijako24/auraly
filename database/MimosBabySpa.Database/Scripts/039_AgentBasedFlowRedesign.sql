-- =============================================================================
-- 039: Rediseño Agent-based — agent_reserva (type 10) absorbe toda la reserva
--
-- DE (~24 nodos)                       A (19 nodos)
-- ─────────────────────────────────── ──────────────────────────────────────
-- collect_service                      agent_reserva  (pipeline: check_avail
-- offer_addons                                         + resolve_pricing)
-- collect_date                         show_confirmation
-- check_availability                   accept_booking  (goto_node target)
-- show_alternatives
-- collect_identity
-- resolve_pricing
-- show_confirmation
-- detect_confirmation
-- reschedule_reservation          →    reschedule_reservation  (se conserva)
--
-- Mecánica clave (restart-from-Start cada turno):
--   1. extract_modern detecta intenciones + variables.
--   2. main_router enruta.
--   3. agent_reserva corre pipeline; cuando steps completan → "completed"
--      → show_confirmation (muestra resumen, setFlag: confirmation_summary_presented=true).
--   4. Siguiente turno: si extract_modern detecta user_confirmed_booking (stageCondition)
--      → PendingJumpNodeId = "accept_booking" (via goto_node en intentionSchema).
--   5. accept_booking (type 4, waitForUser=false) resetea flags → reschedule_reservation
--      → (skipped si !is_rescheduling) → generate_payment_link → wait_payment.
--   6. Webhook pago: payment_confirmed=true → turno sintético → main_router
--      (payment_done) → create_reservation.
--
-- Cambios en intentionSchema:
--   - user_confirmed_booking.behavior: action "none" → "goto_node: accept_booking"
--   - user_requested_availability.behavior.targetNodeId: "collect_date" → "agent_reserva"
--
-- Cambios en variables (onChange.resetFlags):
--   - service:        + __agentStep:agent_reserva:0, :1, confirmation_summary_presented
--   - desired_date:   + __agentStep:agent_reserva:0, confirmation_summary_presented
--   - desired_time:   + __agentStep:agent_reserva:0, confirmation_summary_presented
--   - selected_add_ons: + __agentStep:agent_reserva:1, confirmation_summary_presented
--
-- @DryRun = 1 → SELECT @Out AS DefinitionJson; sin actualizar BD.
-- @DryRun = 0 → UPDATE FlowDefinitions.
-- Idempotente: si ya existe nodo "agent_reserva", omite.
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
    RAISERROR(N'039: No se encontró FlowDefinitionId.', 16, 1);
    RETURN;
END;

DECLARE @Json NVARCHAR(MAX);
SELECT @Json = fd.DefinitionJson
FROM dbo.FlowDefinitions fd
WHERE fd.FlowDefinitionId = @FlowDefinitionId;

IF @Json IS NULL OR ISJSON(@Json) = 0
BEGIN
    RAISERROR(N'039: DefinitionJson inválido o vacío.', 16, 1);
    RETURN;
END;

-- Idempotencia
IF EXISTS (SELECT 1 FROM OPENJSON(@Json, N'$.nodes') WHERE JSON_VALUE(value, N'$.id') = N'agent_reserva')
BEGIN
    PRINT N'039: Nodo agent_reserva ya existe — migración omitida (idempotente).';
    RETURN;
END;

-- ═══════════════════════════════════════════════════════════════════════════════
-- NODOS — 19 nodos (vs ~24 anteriores)
-- ═══════════════════════════════════════════════════════════════════════════════
-- IMPORTANTE: sin CAST(... AS NVARCHAR(MAX)), la cadena de literales N'...' + N'...'
-- se evalúa como NVARCHAR(4000) y SE TRUNCA (ISJSON falla con JSON cortado).
-- ═══════════════════════════════════════════════════════════════════════════════

DECLARE @NodesNew NVARCHAR(MAX);
SET @NodesNew = CAST(N'' AS NVARCHAR(MAX)) + N'['

-- ── Control ──────────────────────────────────────────────────────────────────
+ N'{"id":"start","type":0,"label":"Inicio","config":{"_ui":{"x":40,"y":300}}},'

-- ── Extracción moderna (type 8) ───────────────────────────────────────────────
+ N'{"id":"extract_modern","type":8,"label":"Extracción (IA)","config":{"catalogKey":"extract","_ui":{"x":220,"y":300}}},'

-- ── Router principal (type 9) ─────────────────────────────────────────────────
-- Rutas: payment_confirmed → create_reservation
--        is_information_query → info_response
--        default → agent_reserva
+ N'{"id":"main_router","type":9,"label":"Enrutar","config":{'
    + N'"routes":['
        + N'{"when":{"type":"flag_true","flag":"payment_confirmed"},"port":"payment_done"},'
        + N'{"when":"is_information_query","port":"information"}'
    + N'],'
    + N'"defaultPort":"reserva"'
+ N'},"_ui":{"x":420,"y":300}},'

-- ── Info (type 4) ─────────────────────────────────────────────────────────────
+ N'{"id":"info_response","type":4,"label":"Responder info","config":{'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"El usuario pide informaci\u00f3n espec\u00edfica - responde usando el cat\u00e1logo y FAQ.\r\n\r\nSi NO pregunta por un plan concreto: presenta CATEGOR\u00cdAS (Baby Spa, Estimulaci\u00f3n Temprana, Materno Spa, etc.). Si conoces edad del beb\u00e9, indica qu\u00e9 se adapta. No detalles un solo plan.\r\nSi pregunta por UN plan espec\u00edfico: detalla SOLO ese plan (qu\u00e9 incluye, beneficios, precio exacto).\r\nSi explora una categor\u00eda: presenta planes de esa categor\u00eda. Mayor valor primero. Para cada plan incluye los add-ons (Cumplemes, etc.) con precios exactos. NO inventes.",'
    + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8","BBD27A73-02D7-4F8D-A80A-2928F3A8BC03"],'
    + N'"_ui":{"x":620,"y":60}'
+ N'}},'

-- ── Agente Reservas (type 10) ─────────────────────────────────────────────────
-- Pipeline: check_availability (step 0) + resolve_pricing (step 1)
-- collect: recolecta todos los campos necesarios conversacionalmente
+ N'{"id":"agent_reserva","type":10,"label":"Agente Reservas","config":{'
    + N'"catalogKey":"agent",'
    + N'"collect":{'
        + N'"fields":["service","desired_date","desired_time","selected_add_ons","customer_name","email","baby_name","baby_age"],'
        + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8"],'
        + N'"instructions":"El sistema indica si es el primer mensaje en CONTEXTO DE SESI\u00d3N.\r\n\r\n> PRIMER MENSAJE (Primera interacci\u00f3n: Si):\r\n  - Saluda con calidez y pres\u00e9ntate.\r\n  - Presenta SIEMPRE las categor\u00edas: Planes Baby Spa, Talleres Estimulaci\u00f3n Temprana, Materno Spa, Dulce Espera, Programa Iniciaci\u00f3n al Jard\u00edn.\r\n  - Si conoces la edad del beb\u00e9, menciona qu\u00e9 categor\u00edas se adaptan mejor.\r\n  - NUNCA digas <<alg\u00fan otro servicio>> - nombra las opciones expl\u00edcitamente.\r\n\r\n> CLIENTE RECURRENTE (Cliente recurrente: Si):\r\n  - NO te presentes de nuevo. Saluda reconociendo que regresa.\r\n  - Si solo saluda -> pregunta en qu\u00e9 puedes ayudarle.\r\n\r\n> RECOLECCI\u00d3N DE DATOS (conversaci\u00f3n en curso):\r\n  - Pide datos UNO A LA VEZ, de forma conversacional y c\u00e1lida.\r\n  - Orden natural: 1) servicio -> 2) fecha y hora -> 3) extras Cumplemes (SOLO despu\u00e9s de confirmar disponibilidad) -> 4) nombre, email, beb\u00e9.\r\n  - Si la disponibilidad fue confirmada en este turno (context indica disponibilidad confirmada): ofrece Cumplemes naturalmente (sencilla $35.000 o bouquet $55.000) y pregunta si desea agregar alguno o escribe <<ninguno>>.\r\n  - Cuando haya horarios disponibles (no exactos): pres\u00e9ntalos y pide que elija.\r\n  - NO pidas datos personales antes de confirmar disponibilidad y extras.\r\n  - NO inventes precios ni informaci\u00f3n - usa el cat\u00e1logo disponible."'
    + N'},'
    + N'"actionPipeline":['

        -- Step 0: check_availability
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

        -- Step 1: resolve_pricing
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
    + N'"instructions":"ESTADO: TODOS LOS DATOS RECOPILADOS Y PRECIO CALCULADO.\r\nPresenta el resumen completo de la reserva y pide confirmaci\u00f3n al cliente.\r\n\r\n*Resumen de tu reserva:*\r\n{{collected_data}}\r\n\r\n\u00bfConfirmas todos estos datos para proceder con el pago?\r\n(Responde <<Si>> para confirmar o cu\u00e9ntame qu\u00e9 deseas cambiar.)",'
    + N'"knowledgeSourceIds":["65D838C3-0FC2-41B9-822C-EDD479E545F8"],'
    + N'"completionPort":"completed",'
    + N'"waitForUser":true,'
    + N'"_ui":{"x":620,"y":300}'
+ N'}},'

-- ── Mostrar resumen (type 4) ──────────────────────────────────────────────────
-- Destino desde agent_reserva(completed). Mantiene confirmation_summary_presented=true.
-- Cada turno sin confirmación vuelve aquí vía: main_router→agent_reserva→completed→show_confirmation.
+ N'{"id":"show_confirmation","type":4,"label":"Mostrar resumen","config":{'
    + N'"setFlags":{"confirmation_summary_presented":true},'
    + N'"responseMode":"template",'
    + N'"waitForUser":true,'
    + N'"instructions":"*Resumen de tu reserva:*\r\n\r\n{{collected_data}}\r\n\r\n\u00bfConfirmas estos datos? [Si/No]",'
    + N'"_ui":{"x":900,"y":300}'
+ N'}},'

-- ── Aceptar reserva (type 4, passthrough) ────────────────────────────────────
-- Destino de goto_node desde user_confirmed_booking (PendingJumpNodeId).
-- Resetea flags del pipeline y de confirmación; no genera respuesta.
+ N'{"id":"accept_booking","type":4,"label":"Confirmar reserva","config":{'
    + N'"setFlags":{'
        + N'"confirmation_summary_presented":false,'
        + N'"__agentStep:agent_reserva:0":false,'
        + N'"__agentStep:agent_reserva:1":false'
    + N'},'
    + N'"responseMode":"template",'
    + N'"waitForUser":false,'
    + N'"instructions":"",'
    + N'"_ui":{"x":1100,"y":300}'
+ N'}},'

-- ── Reagendar (type 2) ────────────────────────────────────────────────────────
+ N'{"id":"reschedule_reservation","type":2,"label":"Reagendar cita","config":{'
    + N'"executeWhen":{"type":"FlagIsTrue","parameters":{"flag":"is_rescheduling"}},'
    + N'"action_type":"reschedule",'
    + N'"input_mapping":{"reservation_id":"{{variables.reservation_id}}","date":"{{variables.desired_date}}","time":"{{variables.desired_time}}"},'
    + N'"output_mapping":{"flag:reservation_created":"success"},'
    + N'"_ui":{"x":1300,"y":180}'
+ N'}},'

-- ── Generar link de pago (type 2) ─────────────────────────────────────────────
+ N'{"id":"generate_payment_link","type":2,"label":"Generar link de pago","config":{'
    + N'"action_type":"generate_payment_link",'
    + N'"input_mapping":{"item":"{{variables.service}}","attributes":"{{variables_group:business}}"},'
    + N'"output_mapping":{"payment_link_url":"link_url","payment_reference_id":"reference_id"},'
    + N'"payment":{"requiresAnticipo":false,"anticipoPercentage":50,"currency":"COP","expirationMinutes":1440},'
    + N'"_ui":{"x":1300,"y":300}'
+ N'}},'

-- ── Esperar pago (type 5) ─────────────────────────────────────────────────────
+ N'{"id":"wait_payment","type":5,"label":"Esperar pago","config":{'
    + N'"event_type":"payment_confirmed",'
    + N'"waitingMessage":"Para confirmar tu reserva, realiza el anticipo del 50% usando el siguiente link de pago seguro:\r\n\r\n> {{variables.payment_link_url}}\r\n\r\nUna vez confirmado el pago, tu reserva quedar\u00e1 asegurada autom\u00e1ticamente.\r\nSi ya realizaste el pago, escr\u00edbenos \u00abya pagu\u00e9\u00bb para verificarlo.\r\nSi el link no funciona o expir\u00f3, escribe \u00abnuevo link\u00bb y te enviamos uno actualizado.",'
    + N'"localIntentions":['
        + N'{"key":"user_says_paid","description":"El usuario afirma que ya realiz\u00f3 el pago","detectionExamples":["ya pagu\u00e9","listo el pago","ya transfer\u00ed","hice el pago"],"behavior":{"action":"advance_port","targetPort":"user_claims_done"}},'
        + N'{"key":"user_wants_new_link","description":"El usuario pide un nuevo link de pago","detectionExamples":["otro link","no funciona el link","m\u00e1ndame otro link"],"behavior":{"action":"advance_port","targetPort":"new_link_requested"}}'
    + N'],'
    + N'"instructions":"ESTADO: ESPERANDO CONFIRMACI\u00d3N DE PAGO. El link ya fue enviado.\r\nREGLAS:\r\n- Si dice que ya pag\u00f3 -> <<Perfecto, el sistema verificar\u00e1 autom\u00e1ticamente. Puede tardar unos minutos.>>\r\n- Si el link expir\u00f3 o pide otro -> indica que escriba <<nuevo link>>.\r\n- [X] PROHIBIDO afirmar que la reserva est\u00e1 confirmada.\r\n- [X] PROHIBIDO mostrar ni inventar links de pago.",'
    + N'"_ui":{"x":1540,"y":300}'
+ N'}},'

-- ── Verificar pago (type 2) ───────────────────────────────────────────────────
+ N'{"id":"verify_payment","type":2,"label":"Verificar pago","config":{'
    + N'"action_type":"verify_payment",'
    + N'"input_mapping":{"reference_id":"{{variables.payment_reference_id}}"},'
    + N'"output_mapping":{"flag:payment_confirmed":"confirmed"},'
    + N'"_ui":{"x":1780,"y":180}'
+ N'}},'

-- ── Pago no encontrado (type 4) ───────────────────────────────────────────────
+ N'{"id":"payment_not_found","type":4,"label":"Pago no encontrado","config":{'
    + N'"responseMode":"template",'
    + N'"waitForUser":true,'
    + N'"instructions":"... A\u00fan no encontramos tu pago registrado.\r\n\r\nPuede tardar unos minutos en procesarse. Opciones:\r\n- Espera unos minutos y escr\u00edbenos <<ya pagu\u00e9>>.\r\n- Usa el link si a\u00fan est\u00e1 activo: {{variables.payment_link_url}}\r\n- Escribe <<nuevo link>> si necesitas uno actualizado.\r\n\r\nEstamos pendientes para confirmarte.",'
    + N'"_ui":{"x":1780,"y":380}'
+ N'}},'

-- ── Crear reserva (type 2) ────────────────────────────────────────────────────
+ N'{"id":"create_reservation","type":2,"label":"Crear reserva","config":{'
    + N'"action_type":"create_reservation",'
    + N'"input_mapping":{"item":"{{variables.service}}","date":"{{variables.desired_date}}","time":"{{variables.desired_time}}","customer_name":"{{variables.customer_name}}","customer_email":"{{variables.email}}","customer_phone":"{{variables.phone}}","attributes":"{{variables_group:business}}","selected_add_ons":"{{variables.selected_add_ons}}"},'
    + N'"output_mapping":{"reservation_id":"reservation_id","flag:reservation_created":"success"},'
    + N'"_ui":{"x":2020,"y":300}'
+ N'}},'

-- ── Confirmación exitosa (type 4) ─────────────────────────────────────────────
+ N'{"id":"success_response","type":4,"label":"Confirmaci\u00f3n exitosa","config":{'
    + N'"responseMode":"template",'
    + N'"waitForUser":false,'
    + N'"instructions":"*\u00a1Reserva confirmada!*\r\n\r\nN\u00famero de reserva: #{{variables.reservation_id}}\r\nServicio: {{variables.service}}\r\nFecha: {{variables.desired_date}} a las {{variables.desired_time}}\r\nBeb\u00e9: {{variables.baby_name}}\r\nCliente: {{variables.customer_name}}\r\nEmail: {{variables.email}}\r\n\r\n\u00a1Te esperamos con mucho cari\u00f1o en Mimo''s Baby Spa!\r\nSi necesitas cambiar tu cita o tienes alguna pregunta, escr\u00edbenos con gusto.",'
    + N'"_ui":{"x":2260,"y":300}'
+ N'}},'

-- ── Cancelar (type 4) ─────────────────────────────────────────────────────────
+ N'{"id":"cancel_response","type":4,"label":"Cancelar","config":{'
    + N'"setVariables":{"service":null,"desired_date":null,"desired_time":null,"available_time_slots":null,"reservation_id":null,"payment_reference_id":null,"payment_link_url":null,"selected_add_ons":null,"service_price":null,"addons_detail":null,"total_price":null,"total_price_invariant":null},'
    + N'"setFlags":{"availability_confirmed":false,"reservation_confirmed":false,"add_ons_offered":false,"confirmation_summary_presented":false,"is_rescheduling":false,"__agentStep:agent_reserva:0":false,"__agentStep:agent_reserva:1":false},'
    + N'"responseMode":"llm",'
    + N'"waitForUser":true,'
    + N'"instructions":"El cliente decidi\u00f3 no continuar con el proceso de reserva.\r\n- Acepta sin presionar ni insistir.\r\n- Agradece su tiempo con calidez.\r\n- Ofrece comenzar de nuevo cuando lo desee.\r\n- Cierra la conversaci\u00f3n de forma amable y sin presi\u00f3n.",'
    + N'"_ui":{"x":620,"y":520}'
+ N'}},'

-- ── Configurar reagendamiento (type 2) ────────────────────────────────────────
+ N'{"id":"reschedule_setup","type":2,"label":"Configurar reagendamiento","config":{'
    + N'"action_type":"setup_reschedule",'
    + N'"output_mapping":{"reservation_id":"original_reservation_id","service":"original_service","flag:is_rescheduling":"success"},'
    + N'"instructions":"El cliente quiere reagendar su cita existente.",'
    + N'"_ui":{"x":840,"y":520}'
+ N'}},'

-- ── Pausar reserva (type 2) ───────────────────────────────────────────────────
+ N'{"id":"hold_handler","type":2,"label":"Pausar reserva","config":{'
    + N'"action_type":"suspend",'
    + N'"input_mapping":{"reservation_id":"{{variables.reservation_id}}"},'
    + N'"onSuccessTemplate":"Tu reserva est\u00e1 en pausa. Cuando quieras reagendar, escr\u00edbenos y con gusto te ayudamos.",'
    + N'"onFailureTemplate":"No encontr\u00e9 una reserva activa para pausar. \u00bfQuieres hablar con una asesora?",'
    + N'"_ui":{"x":1060,"y":520}'
+ N'}},'

-- ── Escalar (type 6) ──────────────────────────────────────────────────────────
+ N'{"id":"escalate","type":6,"label":"Escalar a humano","config":{'
    + N'"reason":"El cliente solicit\u00f3 atenci\u00f3n personalizada o el sistema no pudo completar el proceso autom\u00e1ticamente.",'
    + N'"contacts":[],'
    + N'"escalationMessage":"Te estoy conectando con un asesor. Un momento por favor.",'
    + N'"_ui":{"x":2500,"y":420}'
+ N'}},'

-- ── Fin (type 7) ──────────────────────────────────────────────────────────────
+ N'{"id":"end","type":7,"label":"Fin del flujo","config":{"_ui":{"x":2740,"y":300}}}'

+ N']';

IF ISJSON(@NodesNew) = 0
BEGIN
    -- Diagnóstico: muestra el string para identificar el problema
    SELECT LEN(@NodesNew) AS NodeJsonLen,
           LEFT(@NodesNew, 4000) AS Part1,
           SUBSTRING(@NodesNew, 4001, 4000) AS Part2,
           SUBSTRING(@NodesNew, 8001, 4000) AS Part3;
    RAISERROR(N'039: nodes JSON invalido -- abortar.', 16, 1);
    RETURN;
END;

-- ═══════════════════════════════════════════════════════════════════════════════
-- ARISTAS — 29 aristas
-- ═══════════════════════════════════════════════════════════════════════════════

DECLARE @EdgesNew NVARCHAR(MAX);
SET @EdgesNew = CAST(N'' AS NVARCHAR(MAX)) + N'['

-- Troncal
+ N'{"id":"e039_st_ex","sourceNodeId":"start","targetNodeId":"extract_modern"},'
+ N'{"id":"e039_ex_mr","sourceNodeId":"extract_modern","targetNodeId":"main_router"},'

-- main_router
+ N'{"id":"e039_mr_pd","sourceNodeId":"main_router","targetNodeId":"create_reservation","portId":"payment_done"},'
+ N'{"id":"e039_mr_in","sourceNodeId":"main_router","targetNodeId":"info_response","portId":"information"},'
+ N'{"id":"e039_mr_rs","sourceNodeId":"main_router","targetNodeId":"agent_reserva","portId":"reserva"},'

-- agent_reserva
+ N'{"id":"e039_ar_sc","sourceNodeId":"agent_reserva","targetNodeId":"show_confirmation","portId":"completed"},'
+ N'{"id":"e039_ar_es","sourceNodeId":"agent_reserva","targetNodeId":"escalate","portId":"failure"},'

-- accept_booking (destino via goto_node desde user_confirmed_booking)
+ N'{"id":"e039_ab_rr","sourceNodeId":"accept_booking","targetNodeId":"reschedule_reservation"},'

-- reschedule_reservation
+ N'{"id":"e039_rr_sr","sourceNodeId":"reschedule_reservation","targetNodeId":"success_response","portId":"success"},'
+ N'{"id":"e039_rr_gp","sourceNodeId":"reschedule_reservation","targetNodeId":"generate_payment_link","portId":"skipped"},'
+ N'{"id":"e039_rr_es","sourceNodeId":"reschedule_reservation","targetNodeId":"escalate","portId":"failure"},'

-- generate_payment_link
+ N'{"id":"e039_gp_wp","sourceNodeId":"generate_payment_link","targetNodeId":"wait_payment","portId":"success"},'
+ N'{"id":"e039_gp_cr","sourceNodeId":"generate_payment_link","targetNodeId":"create_reservation","portId":"not_required"},'
+ N'{"id":"e039_gp_es","sourceNodeId":"generate_payment_link","targetNodeId":"escalate","portId":"failure"},'

-- wait_payment
+ N'{"id":"e039_wp_cr","sourceNodeId":"wait_payment","targetNodeId":"create_reservation","portId":"received"},'
+ N'{"id":"e039_wp_vp","sourceNodeId":"wait_payment","targetNodeId":"verify_payment","portId":"user_claims_done"},'
+ N'{"id":"e039_wp_gp","sourceNodeId":"wait_payment","targetNodeId":"generate_payment_link","portId":"new_link_requested"},'

-- verify_payment
+ N'{"id":"e039_vp_cr","sourceNodeId":"verify_payment","targetNodeId":"create_reservation","portId":"success"},'
+ N'{"id":"e039_vp_pn","sourceNodeId":"verify_payment","targetNodeId":"payment_not_found","portId":"failure"},'

-- payment_not_found
+ N'{"id":"e039_pn_wp","sourceNodeId":"payment_not_found","targetNodeId":"wait_payment"},'

-- create_reservation
+ N'{"id":"e039_cr_sr","sourceNodeId":"create_reservation","targetNodeId":"success_response","portId":"success"},'
+ N'{"id":"e039_cr_es","sourceNodeId":"create_reservation","targetNodeId":"escalate","portId":"failure"},'

-- success_response
+ N'{"id":"e039_sr_en","sourceNodeId":"success_response","targetNodeId":"end"},'

-- cancel_response
+ N'{"id":"e039_ca_st","sourceNodeId":"cancel_response","targetNodeId":"start"},'

-- reschedule_setup
+ N'{"id":"e039_rs_ar","sourceNodeId":"reschedule_setup","targetNodeId":"agent_reserva","portId":"success"},'
+ N'{"id":"e039_rs_es","sourceNodeId":"reschedule_setup","targetNodeId":"escalate","portId":"failure"},'

-- hold_handler
+ N'{"id":"e039_hh_en","sourceNodeId":"hold_handler","targetNodeId":"end","portId":"success"},'
+ N'{"id":"e039_hh_es","sourceNodeId":"hold_handler","targetNodeId":"escalate","portId":"failure"},'

-- escalate
+ N'{"id":"e039_es_en","sourceNodeId":"escalate","targetNodeId":"end"}'

+ N']';

IF ISJSON(@EdgesNew) = 0
BEGIN
    RAISERROR(N'039: edges JSON inválido — abortar.', 16, 1);
    RETURN;
END;

-- ═══════════════════════════════════════════════════════════════════════════════
-- Construir @Out: preservar variables, intentionSchema, sessionConfig, engineSettings,
--                extractionInstructions del JSON actual + reemplazar nodos y aristas
-- ═══════════════════════════════════════════════════════════════════════════════

DECLARE @Out NVARCHAR(MAX) = @Json;
SET @Out = JSON_MODIFY(@Out, N'$.nodes', JSON_QUERY(@NodesNew));
SET @Out = JSON_MODIFY(@Out, N'$.edges', JSON_QUERY(@EdgesNew));

-- ═══════════════════════════════════════════════════════════════════════════════
-- Actualizar onChange.resetFlags en variables clave
-- Añadir: __agentStep:agent_reserva:0/1 y confirmation_summary_presented
-- para que los cambios de datos reactiven el pipeline y la confirmación.
-- IMPORTANTE: verificar existencia antes de append para evitar duplicados.
-- ═══════════════════════════════════════════════════════════════════════════════

DECLARE @VarCount INT = (SELECT COUNT(*) FROM OPENJSON(JSON_QUERY(@Out, N'$.variables')));
DECLARE @Vi INT = 0;
DECLARE @VKey NVARCHAR(200);
DECLARE @VPath NVARCHAR(200);
DECLARE @ExistingFlags NVARCHAR(MAX);

WHILE @Vi < @VarCount
BEGIN
    SET @VKey  = JSON_VALUE(@Out, N'$.variables[' + CAST(@Vi AS NVARCHAR(12)) + N'].key');
    SET @VPath = N'$.variables[' + CAST(@Vi AS NVARCHAR(12)) + N']';
    SET @ExistingFlags = ISNULL(
        CAST(JSON_QUERY(@Out, @VPath + N'.onChange.resetFlags') AS NVARCHAR(MAX)),
        N'[]');

    IF @VKey = N'service'
    BEGIN
        IF CHARINDEX(N'"__agentStep:agent_reserva:0"', @ExistingFlags) = 0
            SET @Out = JSON_MODIFY(@Out, N'append ' + @VPath + N'.onChange.resetFlags', N'__agentStep:agent_reserva:0');
        IF CHARINDEX(N'"__agentStep:agent_reserva:1"', @ExistingFlags) = 0
            SET @Out = JSON_MODIFY(@Out, N'append ' + @VPath + N'.onChange.resetFlags', N'__agentStep:agent_reserva:1');
        IF CHARINDEX(N'"confirmation_summary_presented"', @ExistingFlags) = 0
            SET @Out = JSON_MODIFY(@Out, N'append ' + @VPath + N'.onChange.resetFlags', N'confirmation_summary_presented');
    END;

    IF @VKey IN (N'desired_date', N'desired_time')
    BEGIN
        IF CHARINDEX(N'"__agentStep:agent_reserva:0"', @ExistingFlags) = 0
            SET @Out = JSON_MODIFY(@Out, N'append ' + @VPath + N'.onChange.resetFlags', N'__agentStep:agent_reserva:0');
        IF CHARINDEX(N'"confirmation_summary_presented"', @ExistingFlags) = 0
            SET @Out = JSON_MODIFY(@Out, N'append ' + @VPath + N'.onChange.resetFlags', N'confirmation_summary_presented');
    END;

    IF @VKey = N'selected_add_ons'
    BEGIN
        IF JSON_QUERY(@Out, @VPath + N'.onChange') IS NULL
            SET @Out = JSON_MODIFY(@Out, @VPath + N'.onChange', JSON_QUERY(N'{"resetFlags":[]}'));
        ELSE IF JSON_QUERY(@Out, @VPath + N'.onChange.resetFlags') IS NULL
            SET @Out = JSON_MODIFY(@Out, @VPath + N'.onChange.resetFlags', JSON_QUERY(N'[]'));

        SET @ExistingFlags = ISNULL(CAST(JSON_QUERY(@Out, @VPath + N'.onChange.resetFlags') AS NVARCHAR(MAX)), N'[]');
        IF CHARINDEX(N'"__agentStep:agent_reserva:1"', @ExistingFlags) = 0
            SET @Out = JSON_MODIFY(@Out, N'append ' + @VPath + N'.onChange.resetFlags', N'__agentStep:agent_reserva:1');
        IF CHARINDEX(N'"confirmation_summary_presented"', @ExistingFlags) = 0
            SET @Out = JSON_MODIFY(@Out, N'append ' + @VPath + N'.onChange.resetFlags', N'confirmation_summary_presented');
    END;

    SET @Vi += 1;
END;

-- ═══════════════════════════════════════════════════════════════════════════════
-- Actualizar intentionSchema
-- ═══════════════════════════════════════════════════════════════════════════════

-- 1. user_confirmed_booking: action "none" → "goto_node: accept_booking"
--    (stageCondition permanece; goto_node usa PendingJumpNodeId en extract_modern)
DECLARE @IntIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_confirmed_booking'
);

IF @IntIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@IntIdx AS NVARCHAR(12)) + N'].behavior',
        JSON_QUERY(N'{"action":"goto_node","targetNodeId":"accept_booking"}'));

-- 2. user_requested_availability: targetNodeId "collect_date" → "agent_reserva"
DECLARE @AvailIdx INT = (
    SELECT TOP 1 TRY_CAST(j.[key] AS INT)
    FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema')) j
    WHERE JSON_VALUE(j.value, N'$.key') = N'user_requested_availability'
);

IF @AvailIdx IS NOT NULL
    SET @Out = JSON_MODIFY(@Out,
        N'$.intentionSchema[' + CAST(@AvailIdx AS NVARCHAR(12)) + N'].behavior.targetNodeId',
        N'agent_reserva');

-- 3. Añadir intenciones globales de pago si no existen en intentionSchema
--    (CHARINDEX sobre @Out fallaría porque "user_says_paid" también aparece
--     en localIntentions del nodo wait_payment; buscar solo en intentionSchema)
IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema'))
    WHERE JSON_VALUE(value, N'$.key') = N'user_says_paid'
)
    SET @Out = JSON_MODIFY(@Out, N'append $.intentionSchema',
        JSON_QUERY(N'{"key":"user_says_paid","description":"El usuario afirma que ya realiz\u00f3 el pago","examples":["ya pagu\u00e9","listo el pago","ya transfer\u00ed","hice el pago"],"priority":6,"behavior":{"action":"goto_node","targetNodeId":"verify_payment"}}'));

IF NOT EXISTS (
    SELECT 1 FROM OPENJSON(JSON_QUERY(@Out, N'$.intentionSchema'))
    WHERE JSON_VALUE(value, N'$.key') = N'user_wants_new_link'
)
    SET @Out = JSON_MODIFY(@Out, N'append $.intentionSchema',
        JSON_QUERY(N'{"key":"user_wants_new_link","description":"El usuario pide un nuevo link de pago","examples":["otro link","no funciona el link","m\u00e1ndame otro link"],"priority":7,"behavior":{"action":"goto_node","targetNodeId":"generate_payment_link"}}'));

-- ═══════════════════════════════════════════════════════════════════════════════
-- Limpiar extractionInstructions: eliminar segundo bloque duplicado
-- "Regla de desambiguaci\u00f3n contextual" (aparece dos veces en el JSON existente).
-- NOTA: JSON_VALUE trunca a 4000 chars; usamos OPENJSON para obtener NVARCHAR(MAX).
-- ═══════════════════════════════════════════════════════════════════════════════

DECLARE @ExtInstr NVARCHAR(MAX) = (
    SELECT TOP 1 j.value
    FROM OPENJSON(@Out) j
    WHERE j.[key] = N'extractionInstructions'
      AND j.[type] = 1
);
IF @ExtInstr IS NOT NULL
BEGIN
    DECLARE @DupMarker NVARCHAR(200) = N'Regla de desambiguaci' + NCHAR(0x00F3) + N'n contextual:';
    DECLARE @FirstPos INT = CHARINDEX(@DupMarker, @ExtInstr);
    IF @FirstPos > 0
    BEGIN
        DECLARE @SecondPos INT = CHARINDEX(@DupMarker, @ExtInstr, @FirstPos + LEN(@DupMarker));
        IF @SecondPos > 0
        BEGIN
            SET @ExtInstr = RTRIM(LEFT(@ExtInstr, @SecondPos - 1));
            SET @Out = JSON_MODIFY(@Out, N'$.extractionInstructions', @ExtInstr);
            PRINT N'039: extractionInstructions - bloque duplicado eliminado.';
        END;
    END;
END;

-- ═══════════════════════════════════════════════════════════════════════════════
-- Dry run o aplicar
-- ═══════════════════════════════════════════════════════════════════════════════

IF @DryRun = 1
BEGIN
    PRINT N'039 DRY RUN OK. FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
    PRINT N'Nodos: 19 (vs ~24 anteriores). Aristas: 29.';
    PRINT N'Cambios en intentionSchema: user_confirmed_booking → goto_node:accept_booking;';
    PRINT N'  user_requested_availability → goto_node:agent_reserva.';
    PRINT N'Pon @DryRun = 0 para aplicar.';
    SELECT @Out AS DefinitionJson;
    RETURN;
END;

UPDATE dbo.FlowDefinitions
   SET DefinitionJson = @Out,
       UpdatedAt      = GETUTCDATE()
WHERE FlowDefinitionId = @FlowDefinitionId;

PRINT N'039: Aplicado. FlowDefinitionId=' + CAST(@FlowDefinitionId AS NVARCHAR(36));
GO
