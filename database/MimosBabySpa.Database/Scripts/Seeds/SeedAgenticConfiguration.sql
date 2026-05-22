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
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "goal": "Conocer el nombre y la edad del bebé, y el plan que prefiere la familia",
        "suggestedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["baby_name", "baby_age_months", "service"]
      },
      {
        "id": "addons_offering",
        "goal": "Ofrecer complementos compatibles con el plan elegido, una sola vez. Si el cliente no desea ninguno, registrar \"ninguno\".",
        "suggestedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["add_ons"]
      },
      {
        "id": "scheduling",
        "goal": "Encontrar y proponer un horario disponible para el servicio elegido",
        "suggestedTools": ["check_availability", "set_fact"],
        "advanceWhenFacts": ["desired_date", "desired_time"]
      },
      {
        "id": "customer_data",
        "goal": "Completar los datos del cliente que aún falten",
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
    { "key": "baby_name",       "label": "nombre del bebé", "type": "string", "required": true,  "source": "user",    "captureMode": "eager" },
    { "key": "baby_age_months", "label": "edad del bebé",   "type": "number", "required": true,  "source": "user",    "captureMode": "eager" },
    { "key": "service",         "label": "plan",            "type": "string", "required": true,  "source": "user" },
    { "key": "add_ons",         "label": "complementos",    "type": "string", "required": false, "source": "user" },
    { "key": "desired_date",    "label": "fecha",           "type": "date",   "required": true,  "source": "user" },
    { "key": "desired_time",    "label": "hora",            "type": "time",   "required": true,  "source": "user" },
    { "key": "customer_name",   "label": "nombre",          "type": "string", "required": true,  "source": "user",    "persistsAcrossConversations": true },
    { "key": "customer_phone",  "label": "teléfono",        "type": "phone",  "required": true,  "source": "channel" },
    { "key": "customer_email",  "label": "email",           "type": "email",  "required": false, "source": "user",    "persistsAcrossConversations": true }
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
        "flag:verbal_confirmation"
      ]
    },
    "assign_paid_slot": {
      "requires": [
        "state:payment_confirmed_no_slot",
        "verification:availability_checked"
      ]
    }
  },
  "messages": {
    "firstTurnGreetingHint": "¡Hola! 😊 Soy Mimi de Mimo''s Baby Spa. Un gusto saludarte. Estoy aqui para ayudarte a elegir el mejor plan para tu bebe.",
    "returningCustomerGreetingHint": "Saluda por su nombre de forma cálida y retoma el flujo de reserva desde el inicio."
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
