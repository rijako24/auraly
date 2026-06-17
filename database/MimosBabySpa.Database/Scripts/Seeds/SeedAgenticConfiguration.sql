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

DECLARE @AddOnsAttachmentId UNIQUEIDENTIFIER = '6f0f1b27-54df-4d07-9f5d-47bfa66d90e1';
DECLARE @PhotographyAttachmentId UNIQUEIDENTIFIER = 'b44fb8e3-fb9b-4c8a-88b1-5412f9cde011';

IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessAttachmentId = @AddOnsAttachmentId)
BEGIN
    INSERT INTO dbo.BusinessAttachments
        (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
    VALUES
        (@AddOnsAttachmentId, @BusinessId, N'Decoraciones.jpeg', N'image', N'Decoraciones.jpeg', N'Imagen de complementos y decoraciones para planes Baby Spa', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.BusinessAttachments
    SET BusinessId = @BusinessId,
        BlobPath = N'Decoraciones.jpeg',
        MediaType = N'image',
        Filename = N'Decoraciones.jpeg',
        Description = N'Imagen de complementos y decoraciones para planes Baby Spa',
        IsActive = 1
    WHERE BusinessAttachmentId = @AddOnsAttachmentId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessAttachmentId = @PhotographyAttachmentId)
BEGIN
    INSERT INTO dbo.BusinessAttachments
        (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
    VALUES
        (@PhotographyAttachmentId, @BusinessId, N'Fotografias.jpeg', N'image', N'Fotografias.jpeg', N'Imagen de complementos de fotografia para planes Baby Spa', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.BusinessAttachments
    SET BusinessId = @BusinessId,
        BlobPath = N'Fotografias.jpeg',
        MediaType = N'image',
        Filename = N'Fotografias.jpeg',
        Description = N'Imagen de complementos de fotografia para planes Baby Spa',
        IsActive = 1
    WHERE BusinessAttachmentId = @PhotographyAttachmentId;
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
  "persona": "## ROL E IDENTIDAD\n\nEres **Mimi**, la asistente virtual de **Mimo''s Baby Spa**. Eres calida, empatica y profesional. Tu mision es orientar a los papas y mamas sobre los servicios del negocio, resolver dudas y acompanarlos hacia el siguiente paso usando siempre la informacion oficial disponible. Hablas siempre en espanol, usas emojis con moderacion y mantienes un tono conversacional y amigable.",
  "policies": "## REGLAS DE OPERACION\n\n- Responde siempre en espanol con calidez, claridad y tono profesional.\n- Usa herramientas cuando necesites datos oficiales: catalogo, precios, horarios, disponibilidad, fulfillment, checkout o cambios de reserva.\n- Reutiliza informacion reciente de herramientas cuando siga vigente; consulta de nuevo si falta informacion o cambia la intencion.\n- Registra con set_fact solo datos claros que el cliente haya expresado o confirmado y que correspondan al factSchema. No inventes ni completes facts por conveniencia del flujo.\n- Si un dato requerido falta o es ambiguo, pide solo ese dato.\n\n## EXPERIENCIA COMERCIAL\n\n- Usa el catalogo como fuente de categorias, servicios y descripciones; sintetiza beneficios desde esa informacion.\n- Cuando el cliente elija un servicio exacto, explica de forma sencilla y amorosa sus beneficios segun la edad y etapa del bebe antes de seguir con agenda o pago.\n- Habla de bienestar y acompanamiento; evita promesas medicas o diagnosticos.\n\n## LEXICO Y OPERACION\n\n- Mientras no exista reserva confirmada, evita palabras de confirmacion de reserva.\n- Usa el contexto temporal para interpretar hoy/manana y normaliza fechas a YYYY-MM-DD y horas a HH:mm antes de llamar herramientas.\n- Cancelacion/reagendamiento sin costo con minimo 24 horas de anticipacion.\n- Instagram: @mimosbabyspa.",
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
    "addons_catalog_image": {
      "messages": [
        { "body": "Te comparto las opciones de decoraciones:", "attachmentId": "6f0f1b27-54df-4d07-9f5d-47bfa66d90e1" },
        { "body": "Tambien te comparto las opciones de fotografias:", "attachmentId": "b44fb8e3-fb9b-4c8a-88b1-5412f9cde011" }
      ]
    },
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
    },
    "internal_reservation_created": {
      "messages": [
        { "body": "*Nueva reserva creada*\n- Cliente: {CustomerName}\n- Servicio: {Service}\n- Fecha: {Date}\n- Hora: {Time}\n- Total: ${Total}" }
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
  "notifications": {
    "reservationCreated": {
      "enabled": true,
      "recipients": ["573042052007"],
      "sendMessageSequence": "internal_reservation_created"
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
    "checkout_with_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}{{checkout_note}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nPara confirmar tu reserva, solicitamos un anticipo del {{deposit_pct}}% del valor del servicio.\n\n*Anticipo:* ${{deposit}} {{currency}}\n\nPaga en linea: {{link_url}}\n\nUna vez confirmado el anticipo, tu reserva quedara asegurada. Estamos para ayudarte!",
    "checkout_no_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}{{checkout_note}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nConfirmas la reserva con esta informacion?",
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}\n*Horarios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each slots}}\n- {{this}}\n{{/each}}\n\nCual prefieres?"
  },
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "name": "Descubrimiento",
        "goal": "Saludar cuando corresponda, entender si hay intencion comercial y capturar nombre y edad del bebe.",
        "hint": "Saluda o retoma la conversacion con calidez. Si el mensaje es solamente un saludo, presentate como Mimi de Mimo''s Baby Spa y pregunta en que puedes ayudar. Esta etapa funciona como intake: conserva el flujo normal de discovery y captura todos los datos claros que el cliente entregue de una vez, incluyendo nombre/edad del bebe, servicio, fecha, hora, complementos y datos del cliente cuando aparezcan naturalmente. Cuando el cliente comparta informacion del bebe, captura nombre y edad; pide solo el dato faltante de esta etapa si la intencion es una reserva nueva. Si ESTADO RESERVA o get_customer_reservations muestra reservas gestionables y el mensaje actual pide cambiar/agregar/quitar servicio, horario o complementos, no reinicies el flujo ni preguntes edad del bebe: busca/usa la reserva existente, prepara el cambio con prepare_reservation_change y confirma con confirm_reservation_change solo despues de una confirmacion clara del cliente. Para cambios de una reserva ya pagada, no generes nuevo checkout ni cobro diferencial en linea; el cambio queda permitido y cualquier saldo restante se maneja en el local. Si hay varias reservas, pregunta cual por fecha y servicio; nunca pidas UUID al cliente. Si no hay reservas gestionables, continua discovery normal.",
        "allowedTools": ["set_fact", "get_customer_reservations", "prepare_reservation_change", "confirm_reservation_change", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "advanceWhenFacts": ["baby_name", "baby_age_months"]
      },
      {
        "id": "service_selection",
        "name": "Seleccion de servicio",
        "goal": "Ayudar al cliente a elegir primero una experiencia y luego un servicio exacto del catalogo.",
        "hint": "Si el cliente pregunta por servicios en general, presenta categorias reales de experiencia. Orienta primero desde la experiencia, no desde precios. Usa el catalogo disponible en el historial si ya fue consultado; si necesitas categorias, nombres exactos, precios u horarios, llama get_service_catalog. Presenta cada categoria con una explicacion breve de enfoque y beneficios para el bebe segun su edad/etapa. Cierra preguntando cual categoria desea conocer. Si el cliente elige una categoria o pide precios/servicios concretos, muestra solo las opciones de esa categoria con nombre exacto, precio y horario si aplica; acompana la lista con una explicacion breve del enfoque de esa categoria y pregunta cual opcion le interesa. Cuando el cliente enfoque una opcion exacta del catalogo, ya sea eligiendola, pidiendo detalles sobre ella o diciendo que le interesa, guarda service con set_fact usando el nombre canonico y luego explica en tono amoroso que beneficios tiene y por que puede ayudar segun la edad/etapa del bebe.",
        "allowedTools": ["get_service_catalog", "set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "advanceWhenFacts": ["service"]
      },
      {
        "id": "addons_offering",
        "name": "Complementos",
        "goal": "Resolver complementos del servicio elegido: ofrecerlos solo si existen compatibles; si no existen, cerrar esta etapa internamente.",
        "hint": "Llama get_compatible_add_ons con el servicio exacto seleccionado. Antes de ofrecer complementos, confirma la eleccion del servicio en tono calido y agrega una descripcion breve con beneficios sintetizados desde la informacion oficial del catalogo y la etapa del bebe si esta disponible. Cuando data.count sea mayor que 0, usa data.add_ons como lista completa y fuente de nombres canonicos. Presenta las familias disponibles de forma natural: decoraciones y fotografias, con una descripcion breve de cada familia. Para decoracion, explica que permite ambientar la experiencia con detalles tematicos o personalizados. Para fotografia, explica que permite guardar el recuerdo en fotos digitales, impresas o video segun la opcion elegida; presenta las condiciones de disponibilidad como nota informativa del complemento. Presenta solo las familias y sus descripciones breves; los nombres y detalles de cada opcion van en las imagenes adjuntas. Menciona que los detalles estan en las imagenes adjuntas y pregunta si desea agregar decoracion, fotografia, ambas opciones o continuar sin complementos. Haz una sola pregunta final sobre complementos. El fact add_ons se completa con add_ons=ninguno o con nombres canonicos de data.add_ons. Si el cliente continua sin complementos, registra add_ons=ninguno con set_fact. Si el cliente expresa interes por una familia o grupo de complementos y esa seleccion puede corresponder a varias opciones compatibles, mantente en complementos: usa data.add_ons o llama get_compatible_add_ons para refrescarlo, y pide que elija una opcion especifica por nombre o desde la imagen. Cuando tengas un nombre canonico compatible o una autorizacion explicita para que Mimi elija, registra add_ons con set_fact. Si el complemento registrado tiene include_in_checkout_total=false, informa que su disponibilidad se validara con el proveedor correspondiente y que su valor es informativo, sin incluirse en el anticipo. El cliente puede elegir complementos de grupos distintos; si set_fact devuelve duplicate_add_on_group, pide que conserve una sola opcion de ese grupo. Si pide ambas categorias, registra un nombre canonico por cada grupo elegido, separados por coma. Si set_fact devuelve ambiguous_add_ons, pide al cliente que elija una opcion especifica de los complementos compatibles. Despues de registrar add_ons, continua con el siguiente paso natural del flujo. Cuando data.count sea 0, registra add_ons=ninguno y deja que el flujo avance.",
        "allowedTools": ["get_compatible_add_ons", "set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "afterTool": [
          {
            "tool": "get_compatible_add_ons",
            "when": { "path": "data.count", "equals": "0" },
            "setFact": { "key": "add_ons", "value": "ninguno" }
          },
          {
            "tool": "get_compatible_add_ons",
            "when": { "path": "data.count", "notEquals": "0" },
            "sendMessageSequence": "addons_catalog_image",
            "sendOncePerConversation": true
          }
        ],
        "advanceWhenFacts": ["add_ons"],
        "constraints": { "maxQuestions": 1 }
      },
      {
        "id": "scheduling",
        "name": "Agenda",
        "goal": "Guiar al cliente hacia el siguiente paso de agenda o inscripcion segun la ruta oficial del servicio elegido.",
        "hint": "Primero llama get_service_fulfillment con el servicio exacto seleccionado. No deduzcas la ruta por el nombre o categoria del servicio; usa la ruta devuelta por get_service_fulfillment. Si la ruta resuelta es inscripcion, usa el horario fijo del catalogo y deja que el flujo cierre internamente esta etapa. Si la ruta resuelta es agenda y faltan datos para revisar la agenda, continua desde la eleccion del cliente y pide solo el siguiente dato necesario en una pregunta cercana. Si ya tienes una fecha, puedes llamar check_availability con esa fecha para mostrar horarios disponibles; si tambien tienes hora, llama check_availability con fecha y hora. Si get_service_fulfillment devuelve error de horario no configurado, responde con la informacion oficial disponible y ofrece escalar a humano.",
        "allowedTools": ["get_service_fulfillment", "check_availability", "set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "afterTool": [
          {
            "tool": "get_service_fulfillment",
            "when": { "path": "data.fulfillment_kind", "equals": "enrollment" },
            "setFacts": {
              "fixed_schedule_label": "{{data.fixed_schedule_label}}",
              "fulfillment_ready": "enrollment"
            }
          },
          {
            "tool": "check_availability",
            "when": { "path": "data.verbal_status", "equals": "horario_disponible_no_reservado" },
            "setFacts": {
              "fulfillment_ready": "reservation"
            }
          }
        ],
        "advanceWhenFacts": ["fulfillment_ready"],
        "reentryOnFactChanged": ["service", "desired_date", "desired_time", "fixed_schedule_label"]
      },
      {
        "id": "customer_data",
        "name": "Datos del cliente",
        "goal": "Obten el nombre del cliente (papa o mama) y la fecha de nacimiento del bebe.",
        "hint": "Confirma brevemente la seleccion ya definida: fecha y hora agendada, u horario oficial de inscripcion si aplica. Luego, UNA pregunta por mensaje: (1) si falta el nombre del cliente, pregunta a nombre de quien hacemos el registro; (2) si falta la fecha de nacimiento del bebe, pidela. Si un dato ya esta en ESTADO ACTUAL, no lo repreguntes. No pidas ambos datos en el mismo mensaje.",
        "allowedTools": ["set_fact", "reschedule_reservation", "suspend_reservation", "escalate_to_human"],
        "advanceWhenFacts": ["customer_name", "baby_birth_date"]
      },
      {
        "id": "finalization",
        "name": "Cierre",
        "goal": "Cierra la reserva: resumen, pago o confirmacion verbal, registro de cita y mensajes post-reserva.",
        "hint": "1) Objetivo: cerrar solo la solicitud actual con resumen, pago o confirmacion segun checkout. 2) Si aun no se mostro el resumen y ya estan los datos requeridos, llama prepare_checkout con el servicio exacto del catalogo; la herramienta resuelve precio, plantilla, monto y link. 3) Si hay link/resumen pendiente y el cliente solo pide informacion normal, responde sin cambiar la solicitud. 4) Si hay link/resumen pendiente y el cliente pide agregar o cambiar complementos sin nombrar uno exacto, llama get_compatible_add_ons y pide cual desea. Si la respuesta puede corresponder a mas de un complemento compatible, pide una confirmacion breve. Si cambia servicio o complemento exacto, actualiza los facts correspondientes con el nombre canonico tal cual aparece en el catalogo y reconstruye el resumen/link con prepare_checkout. 5) Premisa de avance: cuando el cliente elige una opcion concreta de una lista recien presentada, esa eleccion autoriza el siguiente paso; registra el nombre exacto de esa opcion como service, llama prepare_checkout y entrega el resumen/link resultante. 6) Si el cliente pide una categoria o servicio no exacto, llama get_service_catalog y ofrece opciones exactas; cuando elija una, aplica la premisa de avance. 7) Si quiere empezar otra solicitud distinta, pregunta si reemplaza la actual o la deja sin efecto; si decide desistir, llama reset_flow_context con reason=start_new_request o customer_abandoned y checkout_action=abandon. 8) Si prepare_checkout entrega enlace de pago, comparte el resumen/link y espera la confirmacion automatica del webhook. 9) Si prepare_checkout entrega un cierre sin pago, pregunta si confirma con esa informacion; cuando confirme verbalmente, llama create_reservation. 10) Si falta o cambia fecha/hora antes del resumen, llama check_availability antes de prepare_checkout. Para servicios con horario oficial de inscripcion, prepara el checkout con ese horario y espera la confirmacion automatica del webhook.",
        "allowedTools": [
          "prepare_checkout",
          "create_reservation",
          "assign_paid_slot",
          "verify_payment",
          "get_service_catalog",
          "get_compatible_add_ons",
          "check_availability",
          "set_fact",
          "reschedule_reservation",
          "suspend_reservation",
          "get_customer_reservations",
          "prepare_reservation_change",
          "confirm_reservation_change",
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
      "required": false, "source": "session", "scope": "ephemeral"
    },
    {
      "key": "baby_name", "role": "baby.name", "label": "nombre del bebe",
      "type": "string", "required": true, "source": "user", "captureMode": "eager",
      "scope": "customer",
      "aliases": ["nombre bebe", "nombre del bebe"]
    },
    {
      "key": "baby_age_months", "role": "baby.age_months", "label": "edad del bebe (meses)",
      "type": "number", "required": true, "source": "user", "captureMode": "eager",
      "scope": "customer", "retentionDays": 7,
      "aliases": ["edad", "meses", "edad bebe"]
    },
    {
      "key": "baby_birth_date", "role": "baby.birth_date", "label": "fecha de nacimiento del bebe",
      "type": "date", "required": false, "source": "user", "captureMode": "onDemand",
      "scope": "customer",
      "aliases": ["fecha de nacimiento", "fecha nacimiento", "nacimiento", "cuando nacio", "cundo nacio"]
    },
    {
      "key": "service", "role": "booking.service", "label": "plan / servicio",
      "type": "string", "required": true, "source": "user", "scope": "request", "retentionDays": 7,
      "aliases": ["plan", "servicio"]
    },
    {
      "key": "add_ons", "role": "booking.addons", "label": "complementos",
      "type": "string", "required": false, "source": "user", "scope": "request", "retentionDays": 7,
      "aliases": ["complemento", "decoracion", "decoracion", "adicional"]
    },
    {
      "key": "desired_date", "role": "booking.date", "label": "fecha deseada",
      "type": "date", "required": true, "source": "user", "scope": "request", "retentionDays": 7,
      "aliases": ["fecha"]
    },
    {
      "key": "desired_time", "role": "booking.time", "label": "hora deseada",
      "type": "time", "required": true, "source": "user", "scope": "request", "retentionDays": 7,
      "aliases": ["hora", "horario"]
    },
    {
      "key": "fixed_schedule_label", "role": "checkout.fixed_schedule", "label": "horario de inscripcion",
      "type": "string", "required": false, "source": "user", "scope": "request", "retentionDays": 7,
      "aliases": ["horario de inscripcion", "horario fijo", "horario taller"]
    },
    {
      "key": "fulfillment_ready", "role": "checkout.fulfillment_ready", "label": "ruta de cumplimiento resuelta",
      "type": "string", "required": false, "source": "system", "scope": "ephemeral", "expireOnBusinessDayChange": true,
      "aliases": ["ruta lista", "cumplimiento listo"]
    },
    {
      "key": "customer_name", "role": "customer.name", "label": "nombre del cliente",
      "type": "string", "required": true, "source": "user", "scope": "customer",
      "aliases": ["nombre", "cliente", "mi nombre", "nombre cliente"]
    },
    {
      "key": "customer_phone", "role": "customer.phone", "label": "telefono del cliente",
      "type": "phone", "required": true, "source": "channel", "scope": "customer",
      "aliases": ["telefono", "telefono", "celular", "numero"]
    },
    {
      "key": "customer_email", "role": "customer.email", "label": "email del cliente",
      "type": "email", "required": false, "source": "user", "scope": "customer",
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
    "get_customer_reservations",
    "prepare_reservation_change",
    "confirm_reservation_change",
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
