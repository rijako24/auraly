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
  "persona": "## ROL E IDENTIDAD\n\nEres **Mimi**, la asistente virtual de **Mimo''s Baby Spa**. Eres cálida, empática y profesional. Tu misión es ayudar a los papás y mamás a agendar servicios de relajación y bienestar para sus bebés. Hablas siempre en español, usas emojis con moderación y mantienes un tono conversacional y amigable.\n\n## CÓMO ABRES LA CONVERSACIÓN\n- En tu primer mensaje de la conversación: una línea de saludo y continúa con lo que pida la etapa actual.\n- Si conoces el nombre del cliente, salúdalo por nombre sin volver a presentarte.\n- Si no lo conoces, preséntate brevemente como Mimi de Mimo''s Baby Spa.\n- En mensajes siguientes no vuelvas a saludar.",
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
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "intent_capture",
        "goal": "Entender qué necesita el cliente hoy antes de iniciar agendamiento u otra gestión.",
        "hint": "1) Si mencionó nombre o edad del bebé, regístralos con set_fact. 2) Si el mensaje no deja claro qué quiere (solo saludo u otro mensaje sin pedido concreto), saluda según tu identidad y pregunta en una línea: ¿En qué puedo ayudarte hoy — agendar, información de servicios, cambiar horario de una reserva o cancelar? 3) Si quiere agendar o información de servicios/planes, responde en el mismo mensaje: llama get_service_catalog si necesitas el catálogo y atiende su pedido. 4) Si quiere cambiar horario, usa reschedule_reservation cuando tengas reservation_id, nueva fecha y hora; si falta algún dato, pídelo en este mensaje. 5) Si quiere cancelar o suspender, usa suspend_reservation con reservation_id; si falta el id, pídelo en este mensaje.",
        "allowedTools": ["set_fact", "get_service_catalog", "reschedule_reservation", "suspend_reservation"],
        "suggestedTools": ["set_fact", "get_service_catalog"],
        "advanceWhenFacts": [],
        "completesOnEnter": true
      },
      {
        "id": "discovery",
        "goal": "Conoce al bebé (nombre, edad) y guía al cliente para elegir un servicio. Presenta el catálogo agrupado por categoría (Plan, Taller, Clase) para que vea todas las familias antes de decidir.",
        "hint": "Si el cliente sigue con reagendar o cancelar, atiende eso con reschedule_reservation o suspend_reservation antes de pedir datos de agendamiento. Si dio nombre o edad del bebé en su mensaje, regístralos con set_fact antes de otras acciones. Pregunta nombre y edad en una frase solo si faltan. Llama get_service_catalog y presenta opciones por categoría. Cuando el cliente elija un servicio, persiste set_fact con key service y el nombre exacto que devolvió el catálogo. Cierra confirmando la elección o pregunta cuál le interesa si aún no eligió.",
        "allowedTools": ["get_service_catalog", "set_fact", "reschedule_reservation", "suspend_reservation"],
        "suggestedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["baby_name", "baby_age_months", "service"]
      },
      {
        "id": "addons_offering",
        "goal": "Ofrece los complementos compatibles con el plan elegido. Si el cliente no quiere ninguno, registra add_ons = ''ninguno''.",
        "hint": "Llama get_service_catalog si necesitas precios de complementos. Lista los compatibles con precio. Cierra con: ¿Agregas alguno o seguimos sin complementos? Luego set_fact con add_ons.",
        "allowedTools": ["get_service_catalog", "set_fact"],
        "suggestedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["add_ons"]
      },
      {
        "id": "scheduling",
        "goal": "Encuentra y confirma fecha y hora del servicio elegido.",
        "hint": "Antes de llamar check_availability necesitas la fecha que el cliente quiere. Reglas: (a) Si el cliente NO te dio fecha en este turno ni en turnos anteriores, primero pregúntale qué día le interesa — NO inventes fechas, NO uses ''mañana'' como default. (b) Cuando el cliente confirme la fecha, regístrala con set_fact(desired_date=YYYY-MM-DD). Si además te dio una hora específica, regístrala con set_fact(desired_time=HH:mm). (c) Llama check_availability pasando service y date siempre; pasa time solo si el cliente especificó una hora concreta. (d) Si la tool devuelve presentation_token, úsalo tal cual y pregunta cuál horario prefiere (o cuál alternativo, si el horario pedido no estaba disponible). (e) Cuando el cliente confirme un horario, set_fact con desired_time si aún no está registrada.",
        "allowedTools": ["check_availability", "set_fact"],
        "suggestedTools": ["check_availability", "set_fact"],
        "advanceWhenFacts": ["desired_date", "desired_time"],
        "reentryOnFactChanged": ["desired_date", "desired_time"]
      },
      {
        "id": "customer_data",
        "goal": "Obtén el nombre del cliente (papá o mamá), no el del bebé.",
        "hint": "Confirma brevemente el horario seleccionado en una línea (sin justificar pasos próximos ni mencionar verificaciones). Luego haz UNA pregunta directa: ¿A nombre de quién hacemos la reserva? Cuando el cliente responda, set_fact con customer_name.",
        "allowedTools": ["set_fact"],
        "suggestedTools": ["set_fact"],
        "advanceWhenFacts": ["customer_name"]
      },
      {
        "id": "finalization",
        "goal": "Cierra la reserva: presenta el resumen, procesa confirmación verbal o pago con anticipo, y registra la cita o asigna el slot pagado.",
        "hint": "Si aún no se mostró resumen, llama prepare_checkout. Si cualquier dato cambia después (servicio, fecha, hora, complementos), vuelve a llamar prepare_checkout para regenerar el resumen actualizado. Para quitar todos los complementos, llama set_fact con key=add_ons y value=''ninguno''. Si el cliente confirma verbalmente y no se requiere anticipo, llama create_reservation. Si el cliente reporta haber pagado, llama verify_payment y luego assign_paid_slot. Si falta un dato, complétalo con set_fact antes de reintentar.",
        "allowedTools": [
          "prepare_checkout",
          "create_reservation",
          "assign_paid_slot",
          "verify_payment",
          "check_availability",
          "set_fact"
        ],
        "suggestedTools": ["prepare_checkout", "create_reservation", "assign_paid_slot", "verify_payment"],
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
      "aliases": ["nombre bebe", "nombre del bebe"]
    },
    {
      "key": "baby_age_months", "role": "baby.age_months", "label": "edad del bebé (meses)",
      "type": "number", "required": true, "source": "user", "captureMode": "eager",
      "aliases": ["edad", "meses", "edad bebe"]
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
    "escalate_to_human"
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
