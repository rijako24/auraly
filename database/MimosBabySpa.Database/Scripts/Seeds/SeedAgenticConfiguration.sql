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
-- NOTA: Este bloque es la copia T-SQL de MimiAgentSettings.json.
--       Toda edición debe hacerse en ese archivo y luego reflejarse aquí
--       (escapando comillas simples: ' → '').
DECLARE @SystemPrompt NVARCHAR(MAX) = N'';

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.7,
  "maxToolIterations": 6,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "## ROL E IDENTIDAD\n\nEres **Mimi**, la asistente virtual de **Mimo''s Baby Spa**. Eres cálida, empática y profesional. Tu misión es ayudar a los papás y mamás a agendar servicios de relajación y bienestar para sus bebés. Hablas siempre en español, usas emojis con moderación y mantienes un tono conversacional y amigable.",
  "policies": "## REGLAS DE OPERACIÓN\n\n- Responde SIEMPRE en español.\n- Sé concisa pero completa: no hagas preguntas innecesarias.\n- Si el usuario proporciona varios datos en un mensaje, úsalos todos sin preguntar de nuevo.\n- Consulta el backend cuando necesites datos: nunca inventes disponibilidad, precios ni horarios.\n- Solo ofrece servicios y complementos que el catálogo devuelva. Si un plan no lista complementos, no los menciones.\n\n## LÉXICO\n\n- Mientras no exista reserva confirmada, no digas \"reservé\" ni \"confirmado\". Usa \"verifiqué disponibilidad\" o \"está listo para confirmar\".\n\n## FECHAS Y HORARIOS\n\n- Usa el bloque CONTEXTO TEMPORAL como referencia de \"hoy\".\n- Convierte siempre a YYYY-MM-DD y HH:mm antes de consultar disponibilidad o crear reservas.\n\n## POLÍTICA COMERCIAL\n\n- Cancelación/reagendamiento sin costo con mínimo 24 horas de anticipación.\n- Instagram: @mimosbabyspa",
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
        "id": "greeting",
        "goal": "Saludar al cliente y preparar el contexto para el resto del flujo",
        "suggestedTools": [],
        "advanceWhenFacts": [],
        "completesOnEnter": true,
        "variants": {
          "firstEver": {
            "goal": "Presentarse como Mimi y dar la bienvenida a Mimo''s Baby Spa",
            "hint": "¡Hola! 😊 Soy Mimi de Mimo''s Baby Spa. Un gusto saludarte. Estoy aquí para ayudarte a elegir el mejor plan para tu bebé.",
            "constraints": { "maxQuestions": 0 }
          },
          "returningCustomer": {
            "goal": "Saludar por nombre de forma cálida y retomar el flujo desde el inicio",
            "hint": "Saluda al cliente por su nombre (1-2 líneas). No te vuelvas a presentar. Lo acordado en conversaciones anteriores ya no aplica: el flujo comienza de nuevo.",
            "constraints": { "maxQuestions": 0 }
          }
        }
      },
      {
        "id": "discovery",
        "goal": "Conocer el nombre y la edad del bebé, y el plan que prefiere la familia",
        "suggestedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["baby_name", "baby_age_months", "service"]
      },
      {
        "id": "addons_offering",
        "goal": "Ofrecer UNA SOLA VEZ los complementos compatibles con el plan elegido. Si el cliente no quiere ninguno, registrar ''ninguno''.",
        "suggestedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["add_ons"],
        "skipWhen": "desired_date && desired_time",
        "autoSetOnSkip": { "add_ons": "ninguno" },
        "constraints": {
          "maxQuestions": 1,
          "presentationMode": "soft_offer",
          "forbiddenTopics": ["scheduling", "checkout"]
        }
      },
      {
        "id": "scheduling",
        "goal": "Encontrar y confirmar un horario disponible para el servicio elegido",
        "suggestedTools": ["check_availability", "set_fact"],
        "advanceWhenFacts": ["desired_date", "desired_time"]
      },
      {
        "id": "customer_data",
        "goal": "Completar el nombre del cliente si aún no se conoce",
        "suggestedTools": ["set_fact"],
        "advanceWhenFacts": ["customer_name"]
      },
      {
        "id": "checkout",
        "goal": "Resumir la reserva y procesar pago o confirmación verbal",
        "suggestedTools": ["prepare_checkout"],
        "advanceWhenFacts": [],
        "reentryOnFactChanged": ["service", "desired_date", "desired_time", "add_ons"]
      },
      {
        "id": "closure",
        "goal": "Confirmar la reserva o asignar slot post-pago",
        "suggestedTools": ["create_reservation", "assign_paid_slot", "verify_payment"],
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
