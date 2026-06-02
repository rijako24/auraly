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

-- ── AgentType ────────────────────────────────────────────────────────────────
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

-- ── Agent configuration (SettingsJson = source of truth) ─────────────────----
-- NOTA: SettingsJson en este script es la fuente de verdad del agente.
--       Editar aquí (escapar comillas simples: ' → '').
DECLARE @SystemPrompt NVARCHAR(MAX) = N'';

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.7,
  "maxToolIterations": 6,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "## ROL E IDENTIDAD\n\nEres **Mimi**, la asistente virtual de **Mimo''s Baby Spa**. Eres cálida, empática y profesional. Tu misión es ayudar a los papás y mamás a agendar servicios de relajación y bienestar para sus bebés. Hablas siempre en español, usas emojis con moderación y mantienes un tono conversacional y amigable.\n\n## CÓMO ABRES LA CONVERSACIÓN\n- En tu primer mensaje de la conversación: preséntate brevemente como Mimi de Mimo''s Baby Spa y comunica que estás para ayudarle a elegir el mejor plan para su bebé.\n- Si ya conoces el nombre del cliente, salúdalo por nombre y no te vuelvas a presentar.\n- En mensajes siguientes no vuelvas a saludar ni repetir la presentación.",
  "policies": "## REGLAS DE OPERACIÓN\n\n- Responde SIEMPRE en español.\n- Sé concisa pero completa: no hagas preguntas innecesarias.\n- Si el usuario proporciona varios datos en un mensaje, úsalos todos sin preguntar de nuevo.\n- Consulta el backend cuando necesites datos: nunca inventes disponibilidad, precios ni horarios.\n- Solo ofrece servicios y complementos que el catálogo devuelva. Si un plan no lista complementos, no los menciones.\n\n## LÉXICO\n\n- Mientras no exista reserva confirmada, no digas \"reservé\" ni \"confirmado\".\n- Habla de disponibilidad u horarios solo en la etapa scheduling o justo después de check_availability.\n\n## FECHAS Y HORARIOS\n\n- Usa el bloque CONTEXTO TEMPORAL como referencia de \"hoy\".\n- Convierte siempre a YYYY-MM-DD y HH:mm antes de consultar disponibilidad o crear reservas.\n\n## POLÍTICA COMERCIAL\n\n- Cancelación/reagendamiento sin costo con mínimo 24 horas de anticipación.\n- Instagram: @mimosbabyspa",
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
        { "body": "📋 Adjuntamos las indicaciones para tu visita:", "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1" },
        { "body": "Estos son los términos y condiciones:", "attachmentId": "9b2fd590-a2cb-5d8d-a687-493efd0b66a2" }
      ]
    },
    "reservation_confirmed": {
      "messages": [
        { "body": "✅ ¡Tu reserva ha sido confirmada para el {Date} a las {Time}!" },
        { "body": "📋 Adjuntamos las indicaciones para tu visita:", "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1" },
        { "body": "Estos son los términos y condiciones:", "attachmentId": "9b2fd590-a2cb-5d8d-a687-493efd0b66a2" }
      ]
    },
    "payment_slot_taken": {
      "messages": [
        { "body": "✅ Recibimos tu pago de ${amount} {currency}. Tu comprobante quedó registrado." },
        { "body": "Lo sentimos, el horario de las {Time} ya no está disponible porque otro cliente lo reservó primero. Tu pago está seguro. ¿Quieres elegir otro horario? Opciones: {slots}." }
      ]
    }
  },
  "webhooks": {
    "wompi": {
      "reservation_created": { "sendMessageSequence": "reservation_confirmed" },
      "slot_unavailable_after_payment": { "sendMessageSequence": "payment_slot_taken" }
    }
  },
  "templates": {
    "checkout_with_deposit": "📋 *Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Teléfono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebé: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebé: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebé: {{baby_birth_date}}\n{{/if}}\n\n💰 Para confirmar tu reserva, solicitamos un anticipo del {{deposit_pct}}% del valor del servicio.\n\n*Anticipo:* ${{deposit}} {{currency}}\n\n🔗 Paga en línea: {{link_url}}\n\nUna vez confirmado el anticipo, tu reserva quedará asegurada. ¡Estamos para ayudarte!",
    "checkout_no_deposit": "📋 *Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Teléfono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebé: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebé: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebé: {{baby_birth_date}}\n{{/if}}\n\n¿Confirmas la reserva con esta información?",
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}\n📅 *Horarios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each slots}}\n- {{this}}\n{{/each}}\n\n¿Cuál prefieres?"
  },
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "intent_capture",
        "goal": "Entender qué necesita el cliente hoy antes de iniciar agendamiento u otra gestión.",
        "hint": "1) Si el mensaje no deja claro qué quiere (solo saludo u otro mensaje sin pedido concreto): cumple las reglas de apertura de tu identidad (presentación y que estás para ayudarle a elegir el mejor plan para su bebé). Termina con una invitación breve a que te cuente qué necesita. No enumeres opciones ni menciones agendar, cancelar, reagendar, catálogo ni horarios en ese mensaje. 2) Si quiere agendar o información de servicios/planes, responde en el mismo mensaje: llama get_service_catalog si necesitas el catálogo y atiende su pedido. 3) Si quiere cambiar horario o fecha y hay ESTADO RESERVA o RESERVAS DEL CLIENTE, usa reschedule_reservation con new_date y new_time (reservation_id solo si hay varias citas en contexto); nunca pidas UUID ni id al cliente. 4) Si quiere cancelar o suspender y hay reserva en contexto, usa suspend_reservation sin pedir identificadores al cliente.",
        "allowedTools": ["set_fact", "get_service_catalog", "reschedule_reservation", "suspend_reservation"],
        "advanceWhenFacts": [],
        "completesOnEnter": true
      },
      {
        "id": "discovery",
        "goal": "Conocer al bebé (nombre y edad) y que el cliente elija un plan del catálogo. La etapa termina cuando el cliente elige un servicio.",
        "hint": "Si faltan el nombre o la edad del bebé, pregúntalos en una frase. Llama get_service_catalog y presenta opciones por categoría (Plan, Taller, Clase). Cierra según el caso: si AÚN no eligió plan, pregunta cuál le interesa; si ya eligió, confirma la elección en una frase usando el nombre exacto del catálogo. Si pide reagendar o cancelar, usa reschedule_reservation o suspend_reservation según ESTADO RESERVA.",
        "allowedTools": ["get_service_catalog", "set_fact", "reschedule_reservation", "suspend_reservation"],
        "advanceWhenFacts": ["baby_name", "baby_age_months", "service"],
        "constraints": { "maxQuestions": 1 }
      },
      {
        "id": "addons_offering",
        "goal": "Ofrece los complementos compatibles con el plan elegido. Si el cliente no quiere ninguno, el valor de complementos es ''ninguno''.",
        "hint": "Llama get_service_catalog si necesitas precios de complementos. Lista los compatibles con precio. Cierra con: ¿Agregas alguno o seguimos sin complementos?",
        "allowedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["add_ons"],
        "constraints": { "maxQuestions": 1 }
      },
      {
        "id": "scheduling",
        "goal": "Encuentra y confirma fecha y hora del servicio elegido.",
        "hint": "Antes de llamar check_availability necesitas la fecha que el cliente quiere. Reglas: (a) Si el cliente NO te dio fecha en este turno ni en turnos anteriores, primero pregúntale qué día le interesa — NO inventes fechas, NO uses ''mañana'' como default. (b) Llama check_availability pasando service y date siempre; pasa time solo si el cliente especificó una hora concreta. (c) Si la tool devuelve presentation_token, úsalo tal cual y pregunta cuál horario prefiere (o cuál alternativo, si el horario pedido no estaba disponible). (d) El horario que elija el cliente entre los ofrecidos es la hora deseada.",
        "allowedTools": ["check_availability", "set_fact"],
        "advanceWhenFacts": ["desired_date", "desired_time"],
        "reentryOnFactChanged": ["desired_date", "desired_time"]
      },
      {
        "id": "customer_data",
        "goal": "Obtén el nombre del cliente (papá o mamá) y la fecha de nacimiento del bebé.",
        "hint": "Confirma brevemente el horario seleccionado en una línea (sin justificar pasos próximos ni mencionar verificaciones). Luego, UNA pregunta por mensaje: (1) si falta el nombre del cliente → ¿A nombre de quién hacemos la reserva? (2) si falta la fecha de nacimiento del bebé → pídela. Si un dato ya está en ESTADO ACTUAL, no lo repreguntes; si el bebé persistido podría ser otro hijo, confírmalo antes de reusarlo. No pidas ambos datos en el mismo mensaje.",
        "allowedTools": ["set_fact"],
        "advanceWhenFacts": ["customer_name", "baby_birth_date"]
      },
      {
        "id": "finalization",
        "goal": "Cierra la reserva: resumen, pago o confirmación verbal, registro de cita y mensajes post-reserva.",
        "hint": "Si aún no se mostró el resumen, llama prepare_checkout. No inventes horarios; obténlos de check_availability. Si el cliente cambia el servicio, la fecha, la hora o los complementos, vuelve a verificar disponibilidad con check_availability cuando aplique y reconstruye el resumen con prepare_checkout. Cierre sin anticipo: si el cliente confirma verbalmente, llama create_reservation. Cierre con pago: si reporta haber pagado, llama verify_payment y luego assign_paid_slot. Tras create_reservation o assign_paid_slot exitoso, llama send_message_sequence con sequence=''reservation_confirmed''.",
        "allowedTools": [
          "prepare_checkout",
          "create_reservation",
          "assign_paid_slot",
          "verify_payment",
          "check_availability",
          "set_fact",
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
      "key": "baby_name", "role": "baby.name", "label": "nombre del bebé",
      "type": "string", "required": true, "source": "user", "captureMode": "eager",
      "persistsAcrossConversations": true,
      "aliases": ["nombre bebe", "nombre del bebe"]
    },
    {
      "key": "baby_age_months", "role": "baby.age_months", "label": "edad del bebé (meses)",
      "type": "number", "required": true, "source": "user", "captureMode": "eager",
      "aliases": ["edad", "meses", "edad bebe"]
    },
    {
      "key": "baby_birth_date", "role": "baby.birth_date", "label": "fecha de nacimiento del bebé",
      "type": "date", "required": false, "source": "user", "captureMode": "onDemand",
      "persistsAcrossConversations": true,
      "aliases": ["fecha de nacimiento", "fecha nacimiento", "nacimiento", "cuando nacio", "cuándo nació"]
    },
    {
      "key": "service", "role": "booking.service", "label": "plan / servicio",
      "type": "string", "required": true, "source": "user",
      "aliases": ["plan", "servicio"]
    },
    {
      "key": "add_ons", "role": "booking.addons", "label": "complementos",
      "type": "string", "required": false, "source": "user",
      "aliases": ["complemento", "decoracion", "decoración", "adicional"]
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
      "key": "customer_name", "role": "customer.name", "label": "nombre del cliente",
      "type": "string", "required": true, "source": "user",
      "persistsAcrossConversations": true,
      "aliases": ["nombre", "cliente", "mi nombre", "nombre cliente"]
    },
    {
      "key": "customer_phone", "role": "customer.phone", "label": "teléfono del cliente",
      "type": "phone", "required": true, "source": "channel",
      "persistsAcrossConversations": true,
      "aliases": ["telefono", "teléfono", "celular", "numero"]
    },
    {
      "key": "customer_email", "role": "customer.email", "label": "email del cliente",
      "type": "email", "required": false, "source": "user",
      "persistsAcrossConversations": true,
      "aliases": ["email", "correo"]
    }
  ],
  "guards": {
    "prepare_checkout": {
      "requires": [
        "fact:service",
        "fact:desired_date",
        "fact:desired_time",
        "fact:customer_name",
        "verification:availability_checked"
      ]
    },
    "create_reservation": {
      "requires": [
        "verification:availability_checked",
        "verification:customer_identified",
        "verification:checkout_prepared",
        "expr:NOT policy.deposit_required"
      ]
    },
    "assign_paid_slot": {
      "requires": [
        "state:payment_confirmed_no_slot",
        "verification:availability_checked"
      ]
    }
  },
  "enabledTools": [
    "set_fact",
    "get_service_catalog",
    "check_availability",
    "prepare_checkout",
    "create_reservation",
    "assign_paid_slot",
    "reschedule_reservation",
    "suspend_reservation",
    "verify_payment",
    "escalate_to_human",
    "send_message_sequence"
  ],
  "escalation": {
    "contacts": ["+573012926660"]
  }
}';



-- ── Agent (Mimi Bot) ─────────────────────────────────────────────────────────
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

-- ── Vincular WhatsApp del negocio al agente ──────────────────────────────────
UPDATE dbo.BusinessWhatsAppNumbers
SET AgentId = @AgentId
WHERE BusinessId = @BusinessId
  AND (AgentId IS NULL OR AgentId <> @AgentId);

PRINT N'SeedAgenticConfiguration: Mimi Bot configured for business ' + CAST(@BusinessId AS NVARCHAR(36));
GO
