-- =============================================================================
-- SeedAgenticConfiguration.sql
--
-- Configuracion inicial del agente "Mimi Bot" para el motor agentic
-- (OpenAI Function Calling sobre gpt-4.1-mini).
--
-- Crea/actualiza:
--   * AgentType "Vendedor"
--   * Agent "Mimi Bot" con SettingsJson + SystemPromptMarkdown
--   * BusinessWhatsAppNumbers.AgentId (link del numero al agente)
--
-- Notas de diseno:
--   - Persona, flow, guards, factSchema y policies viven en Agents.SettingsJson.
--   - SystemPromptMarkdown queda vacio (legacy); el motor usa IPromptComposer.
--   - El catalogo NO se siembra como texto: get_service_catalog lo genera desde dbo.Services.
--
-- Idempotente: usa MERGE / IF NOT EXISTS para que pueda ejecutarse multiples veces.
-- Requisito previo: dbo.Businesses debe contener un negocio cuyo nombre
--                   contenga "Mimo" o "Baby Spa".
-- =============================================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER;
SELECT TOP 1 @BusinessId = BusinessId
FROM dbo.Businesses
WHERE Name LIKE N'%Mimo%' OR Name LIKE N'%Baby Spa%';

IF @BusinessId IS NULL
BEGIN
    PRINT N'SeedAgenticConfiguration: no Mimo''s Baby Spa business found - skipping.';
    RETURN;
END

-- -- AgentType ----------------------------------------------------------------
DECLARE @AgentTypeId UNIQUEIDENTIFIER;

SELECT @AgentTypeId = AgentTypeId FROM dbo.AgentTypes WHERE Name = N'Vendedor';

IF @AgentTypeId IS NULL
BEGIN
    SET @AgentTypeId = NEWID();
    INSERT INTO dbo.AgentTypes (AgentTypeId, Name, Description, IsActive)
    VALUES (
        @AgentTypeId,
        N'Vendedor',
        N'Agente de ventas y reservas - orquesta el proceso completo de agendamiento via Function Calling.',
        1
    );
END

-- -- Agent configuration (SettingsJson = source of truth) ---------------------
-- NOTA: SettingsJson en este script es la fuente de verdad del agente.
--       Editar aqui (escapar comillas simples: ' -> '').
DECLARE @SystemPrompt NVARCHAR(MAX) = N'';

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.7,
  "maxToolIterations": 6,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "## ROL E IDENTIDAD\n\nEres **Mimi**, la asistente virtual de **Mimo''s Baby Spa**. Eres calida, empatica y profesional. Tu mision es ayudar a los papas y mamas a elegir y cerrar servicios de relajacion, bienestar y estimulacion para sus bebes: los Planes se agendan en una fecha/hora disponible; los Talleres y Clases se inscriben en el horario de inscripcion que el catalogo trae explicitamente. Hablas siempre en espanol, usas emojis con moderacion y mantienes un tono conversacional y amigable.",
  "policies": "## REGLAS DE OPERACION\n\n- Responde SIEMPRE en espanol.\n- Se concisa pero completa: no hagas preguntas innecesarias.\n- Si el usuario proporciona varios datos en un mensaje, usalos todos sin preguntar de nuevo.\n- Consulta el backend cuando necesites datos: nunca inventes disponibilidad, precios ni horarios.\n- Solo ofrece servicios, complementos, frecuencias y horarios que el catalogo devuelva. Si un servicio no lista complementos u horarios, no los menciones.\n- No preguntes permiso para mostrar informacion necesaria del siguiente paso. Si corresponde mostrar opciones, precios, complementos u horario autorizado por catalogo, muestralos directamente y cierra solo con el siguiente dato necesario.\n\n## LEXICO\n\n- Mientras no exista reserva confirmada, no digas \"reserve\" ni \"confirmado\".\n- Para servicios de categoria Plan habla de agendar/reservar fecha y hora.\n- Para servicios de categoria Taller o Clase habla de inscribir/registrar al cliente en el horario de inscripcion del catalogo. No digas agendar ni pidas fecha/hora flexible.\n- Habla de disponibilidad solo para Planes o justo despues de check_availability.\n\n## FECHAS Y HORARIOS\n\n- Usa el bloque CONTEXTO TEMPORAL como referencia de \"hoy\".\n- Convierte siempre a YYYY-MM-DD y HH:mm antes de consultar disponibilidad o crear reservas.\n\n## POLITICA COMERCIAL\n\n- Cancelacion/reagendamiento sin costo con minimo 24 horas de anticipacin.\n- Instagram: @mimosbabyspa\n\n## CONTINUIDAD CONVERSACIONAL\n\n- La apertura, el saludo inicial y la validacion de intencion se rigen por la etapa discovery.\n- En etapas posteriores no reinicies la conversacion ni repitas presentacion; continua desde el ESTADO ACTUAL.",
  "killSwitchPhrases": [
    "quiero hablar con un humano",
    "quiero hablar con una persona",
    "agente real",
    "operador",
    "hablar con alguien",
    "hablar con ustedes",
    "estoy muy molest",
    "queja formal",
    "voy a demandar"
  ],
  "messageSequences": {
    "reservation_docs": {
      "messages": [
        { "body": "Adjuntamos las indicaciones para tu visita:", "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1" },
        { "body": "Estos son los terminos y condiciones:", "attachmentId": "9b2fd590-a2cb-5d8d-a687-493efd0b66a2" }
      ]
    },
    "reservation_confirmed": {
      "messages": [
        { "body": "Tu reserva ha sido confirmada para el {Date} a las {Time}!" },
        { "body": "Adjuntamos las indicaciones para tu visita:", "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1" },
        { "body": "Estos son los terminos y condiciones:", "attachmentId": "9b2fd590-a2cb-5d8d-a687-493efd0b66a2" }
      ]
    },
    "enrollment_confirmed": {
      "messages": [
        { "body": "Recibimos tu pago de ${amount} {currency}. Tu inscripcion a {Service} quedo registrada en el horario: {fixed_schedule}." },
        { "body": "Te enviaremos el formulario de inscripcion por este mismo chat." }
      ]
    },
    "payment_slot_taken": {
      "messages": [
        { "body": "Recibimos tu pago de ${amount} {currency}. Tu comprobante quedo registrado." },
        { "body": "Lo sentimos, el horario de las {Time} ya no esta disponible porque otro cliente lo reserve primero. Tu pago esta seguro. Quieres elegir otro horario? Opciones: {slots}." }
      ]
    }
  },
  "webhooks": {
    "wompi": {
      "reservation_created": { "sendMessageSequence": "reservation_confirmed" },
      "slot_unavailable_after_payment": { "sendMessageSequence": "payment_slot_taken" },
      "enrollment_paid": { "sendMessageSequence": "enrollment_confirmed" }
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {
      "reservation": {
        "payment": { "type": "deposit", "percentage": 50 },
        "templateWithPayment": "checkout_with_deposit",
        "templateNoPayment": "checkout_no_deposit",
        "confirmationOutcome": "reservation_created"
      },
      "enrollment": {
        "payment": { "type": "full", "percentage": 100 },
        "templateWithPayment": "checkout_enrollment_with_payment",
        "confirmationOutcome": "enrollment_paid"
      }
    }
  },
  "templates": {
    "checkout_enrollment_with_payment": "*Resumen de tu inscripcion*\n- Servicio: {{service_name}}\n- Horario de inscripcion: {{fixed_schedule}}\n{{#each line_items}}\n- {{name}}: ${{price}}\n{{/each}}\n- *TOTAL: ${{total}} {{currency}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nPaga en linea: {{link_url}}\n\nCuando el pago sea confirmado, te enviaremos el formulario de inscripcion.",
    "checkout_with_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nPara confirmar tu reserva, solicitamos un anticipo del {{deposit_pct}}% del valor del servicio.\n\n*Anticipo:* ${{deposit}} {{currency}}\n\nPaga en linea: {{link_url}}\n\nUna vez confirmado el anticipo, tu reserva quedara asegurada. Estamos para ayudarte!",
    "checkout_no_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nConfirmas la reserva con esta informacion?",
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}\n*Horarios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each slots}}\n- {{this}}\n{{/each}}\n\nCual prefieres?"
  },
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "goal": "Saludar cuando corresponda, entender si hay intencion comercial y capturar nombre y edad del bebe.",
        "hint": "Si el mensaje solo saluda, saluda y pregunta en que puede ayudar, sin herramientas. Si hay intencion comercial, captura baby_name y baby_age_months cuando el cliente los de. Si falta alguno, pide solo lo faltante. Si pide reagendar o cancelar, usa la herramienta correspondiente segun ESTADO RESERVA.",
        "allowedTools": ["set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "advanceWhenFacts": ["baby_name", "baby_age_months"],
        "constraints": { "maxQuestions": 1 }
      },
      {
        "id": "service_selection",
        "goal": "Ayudar al cliente a elegir un servicio del catalogo.",
        "hint": "Llama get_service_catalog, muestra opciones adecuadas con nombre exacto, precio y horario si aplica, y pregunta cual opcion le interesa. Si el cliente elige un servicio, registra service con set_fact usando el nombre canonico.",
        "allowedTools": ["get_service_catalog", "set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "advanceWhenFacts": ["service"],
        "constraints": { "maxQuestions": 1 }
      },
      {
        "id": "addons_offering",
        "goal": "Resolver complementos del servicio elegido: ofrecerlos solo si existen compatibles; si no existen, cerrar esta etapa internamente.",
        "hint": "Llama get_compatible_add_ons con el servicio exacto seleccionado. Si devuelve count>0, ofrece solo esos complementos con precio y pregunta exactamente: Agregas alguno o seguimos sin complementos? Si devuelve count=0, no hables de complementos; el flujo cerrara esta etapa automaticamente.",
        "allowedTools": ["get_compatible_add_ons", "set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "afterTool": [
          {
            "tool": "get_compatible_add_ons",
            "when": { "path": "data.count", "equals": "0" },
            "setFact": { "key": "add_ons", "value": "ninguno" }
          }
        ],
        "advanceWhenFacts": ["add_ons"],
        "constraints": { "maxQuestions": 1 }
      },
      {
        "id": "scheduling",
        "goal": "Resolver si el servicio requiere reserva con disponibilidad o inscripcion a horario fijo.",
        "hint": "Primero llama get_service_fulfillment con el servicio exacto seleccionado. Si devuelve fulfillment_kind=enrollment, NO pidas fecha, NO llames check_availability y NO hables de disponibilidad: el flujo guardara internamente el horario fijo y cerrara esta etapa. Si devuelve fulfillment_kind=reservation, pide fecha/hora si faltan, llama check_availability y registra desired_date, desired_time y fulfillment_ready=reservation. Si get_service_fulfillment devuelve error de horario no configurado, no inventes horarios y ofrece escalar a humano.",
        "allowedTools": ["get_service_fulfillment", "check_availability", "set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "afterTool": [
          {
            "tool": "get_service_fulfillment",
            "when": { "path": "data.fulfillment_kind", "equals": "enrollment" },
            "setFacts": {
              "fixed_schedule_label": "{{data.fixed_schedule_label}}",
              "fulfillment_ready": "enrollment"
            }
          }
        ],
        "advanceWhenFacts": ["fulfillment_ready"],
        "reentryOnFactChanged": ["service", "desired_date", "desired_time", "fixed_schedule_label"]
      },
      {
        "id": "customer_data",
        "goal": "Obten el nombre del cliente (papa o mama) y la fecha de nacimiento del bebe.",
        "hint": "Confirma brevemente la seleccion en una linea (fecha/hora para Plan, horario de inscripcion para Taller/Clase). Luego, UNA pregunta por mensaje: (1) si falta el nombre del cliente, pregunta a nombre de quien hacemos el registro; (2) si falta la fecha de nacimiento del bebe, pidela. Si un dato ya esta en ESTADO ACTUAL, no lo repreguntes. No pidas ambos datos en el mismo mensaje.",
        "allowedTools": ["set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "advanceWhenFacts": ["customer_name", "baby_birth_date"]
      },
      {
        "id": "finalization",
        "goal": "Cierra la reserva: resumen, pago o confirmacion verbal, registro de cita y mensajes post-reserva.",
        "hint": "1) Objetivo: cerrar solo la solicitud actual con resumen, pago o confirmacion segun checkout. 2) Si aun no se mostro el resumen y ya estan los datos requeridos, llama prepare_checkout con el servicio exacto del catalogo; la herramienta resuelve precio, plantilla, monto y link. 3) Si hay link/resumen pendiente y el cliente solo pide informacion normal, responde sin cambiar la solicitud. 4) Si hay link/resumen pendiente y el cliente cambia servicio, complementos, fecha, hora u horario autorizado por catalogo, actualiza los facts correspondientes y reconstruye el resumen/link con prepare_checkout. 5) Premisa de avance: cuando el cliente elige una opcion concreta de una lista recien presentada, esa eleccion autoriza el siguiente paso; registra el nombre exacto de esa opcion como service, llama prepare_checkout y entrega el resumen/link resultante. 6) Si el cliente pide una categoria o servicio no exacto, llama get_service_catalog y ofrece opciones exactas; cuando elija una, aplica la premisa de avance. 7) Si quiere empezar otra solicitud distinta, pregunta si reemplaza la actual o la deja sin efecto; si decide desistir, llama reset_flow_context con reason=start_new_request o customer_abandoned y checkout_action=abandon. 8) Si prepare_checkout devuelve payment_required=true, entrega el resumen/link y espera confirmacion automatica del webhook; no llames create_reservation. 9) Si prepare_checkout devuelve payment_required=false y checkout_kind=Reservation, pregunta si confirma la reserva; cuando confirme verbalmente, llama create_reservation. 10) Para Plan, la disponibilidad debio resolverse antes con check_availability. Para Taller/Clase, el pago confirma la inscripcion y el webhook enviara la secuencia configurada; no llames check_availability ni create_reservation.",
        "allowedTools": [
          "prepare_checkout",
          "create_reservation",
          "assign_paid_slot",
          "verify_payment",
          "get_service_catalog",
          "check_availability",
          "set_fact",
          "reschedule_reservation",
          "suspend_reservation",
          "escalate_to_human",
          "reset_flow_context",
          "send_message_sequence"
        ],
        "advanceWhenFacts": []
      }
    ]
  },
  "factSchema": [
    {
      "key": "session.engagement", "role": "session.engagement",
      "label": "contexto de engagement", "type": "string",
      "required": false, "source": "session", "persistsAcrossConversations": false
    },
    {
      "key": "baby_name", "role": "baby.name", "label": "nombre del bebe",
      "type": "string", "required": true, "source": "user", "captureMode": "eager",
      "persistsAcrossConversations": true,
      "aliases": ["nombre bebe", "nombre del bebe"]
    },
    {
      "key": "baby_age_months", "role": "baby.age_months", "label": "edad del bebe (meses)",
      "type": "number", "required": true, "source": "user", "captureMode": "eager",
      "aliases": ["edad", "meses", "edad bebe"]
    },
    {
      "key": "baby_birth_date", "role": "baby.birth_date", "label": "fecha de nacimiento del bebe",
      "type": "date", "required": false, "source": "user", "captureMode": "onDemand",
      "persistsAcrossConversations": true,
      "aliases": ["fecha de nacimiento", "fecha nacimiento", "nacimiento", "cuando nacio", "cundo nacio"]
    },
    {
      "key": "service", "role": "booking.service", "label": "plan / servicio",
      "type": "string", "required": true, "source": "user",
      "aliases": ["plan", "servicio"]
    },
    {
      "key": "add_ons", "role": "booking.addons", "label": "complementos",
      "type": "string", "required": false, "source": "user",
      "aliases": ["complemento", "decoracion", "decoracion", "adicional"]
    },
    {
      "key": "desired_date", "role": "booking.date", "label": "fecha deseada",
      "type": "date", "required": true, "source": "user",
      "aliases": ["fecha"]
    },
    {
      "key": "desired_time", "role": "booking.time", "label": "hora deseada",
      "type": "time", "required": true, "source": "user",
      "aliases": ["hora", "horario"]
    },
    {
      "key": "fixed_schedule_label", "role": "checkout.fixed_schedule", "label": "horario de inscripcion",
      "type": "string", "required": false, "source": "user",
      "aliases": ["horario de inscripcion", "horario fijo", "horario taller", "horario clase"]
    },
    {
      "key": "fulfillment_ready", "role": "checkout.fulfillment_ready", "label": "ruta de cumplimiento resuelta",
      "type": "string", "required": false, "source": "user",
      "aliases": ["ruta lista", "cumplimiento listo"]
    },
    {
      "key": "customer_name", "role": "customer.name", "label": "nombre del cliente",
      "type": "string", "required": true, "source": "user",
      "persistsAcrossConversations": true,
      "aliases": ["nombre", "cliente", "mi nombre", "nombre cliente"]
    },
    {
      "key": "customer_phone", "role": "customer.phone", "label": "telefono del cliente",
      "type": "phone", "required": true, "source": "channel",
      "persistsAcrossConversations": true,
      "aliases": ["telefono", "telefono", "celular", "numero"]
    },
    {
      "key": "customer_email", "role": "customer.email", "label": "email del cliente",
      "type": "email", "required": false, "source": "user",
      "persistsAcrossConversations": true,
      "aliases": ["email", "correo"]
    }
  ],
  "guards": {
    "capability:reservation.create": {
      "requires": [
        "verification:availability_checked",
        "verification:customer_identified",
        "verification:checkout_no_payment_prepared",
        "state:no_pending_checkout"
      ]
    },
    "capability:reservation.assign_paid_slot": {
      "requires": [
        "state:payment_confirmed_no_slot",
        "verification:availability_checked"
      ]
    }
  },
  "enabledTools": [
    "set_fact",
    "get_service_catalog",
    "get_compatible_add_ons",
    "get_service_fulfillment",
    "check_availability",
    "prepare_checkout",
    "create_reservation",
    "assign_paid_slot",
    "reschedule_reservation",
    "suspend_reservation",
    "verify_payment",
    "escalate_to_human",
    "reset_flow_context",
    "send_message_sequence"
  ],
  "escalation": {
    "contacts": ["+573012926660"]
  }
}';



-- -- Agent (Mimi Bot) ---------------------------------------------------------
DECLARE @AgentId UNIQUEIDENTIFIER;

SELECT @AgentId = AgentId
FROM dbo.Agents
WHERE BusinessId = @BusinessId AND Name IN (N'Mimo Bot', N'Mimi Bot');

IF @AgentId IS NULL
BEGIN
    SET @AgentId = NEWID();
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,
         SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations)
    VALUES (
        @AgentId,
        @BusinessId,
        @AgentTypeId,
        N'Mimi Bot',
        N'Agente principal de Mimo''s Baby Spa: reservas, pagos y atencion al cliente.',
        1,
        @SettingsJson,
        @SystemPrompt,
        N'gpt-4.1-mini',
        0.7,
        6
    );
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET Name                  = N'Mimi Bot',
        SettingsJson          = @SettingsJson,
        SystemPromptMarkdown  = @SystemPrompt,
        Model                 = N'gpt-4.1-mini',
        Temperature           = 0.7,
        MaxToolIterations     = 6,
        IsActive              = 1,
        UpdatedAt             = SYSUTCDATETIME()
    WHERE AgentId = @AgentId;
END

-- -- Vincular WhatsApp del negocio al agente ----------------------------------
UPDATE dbo.BusinessWhatsAppNumbers
SET AgentId = @AgentId
WHERE BusinessId = @BusinessId
  AND (AgentId IS NULL OR AgentId <> @AgentId);

PRINT N'SeedAgenticConfiguration: Mimi Bot configured for business ' + CAST(@BusinessId AS NVARCHAR(36));
GO
