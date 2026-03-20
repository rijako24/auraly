-- =============================================================================
-- Migration 009: Generic Flow Engine Tables + Mimo's Baby Spa Seed Data
-- =============================================================================

BEGIN TRANSACTION;

-- =============================================================================
-- SECTION 1: NEW TABLES
-- =============================================================================

-- AgentTypes
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AgentTypes')
CREATE TABLE [dbo].[AgentTypes] (
    [AgentTypeId]   UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_AgentTypes] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Name]          NVARCHAR(100)    NOT NULL,
    [Description]   NVARCHAR(500)    NULL,
    [IsActive]      BIT              NOT NULL DEFAULT 1,
    [CreatedAt]     DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

-- Agents
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Agents')
CREATE TABLE [dbo].[Agents] (
    [AgentId]       UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_Agents] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [BusinessId]    UNIQUEIDENTIFIER NOT NULL,
    [AgentTypeId]   UNIQUEIDENTIFIER NOT NULL,
    [Name]          NVARCHAR(200)    NOT NULL,
    [Description]   NVARCHAR(500)    NULL,
    [IsActive]      BIT              NOT NULL DEFAULT 1,
    [SettingsJson]  NVARCHAR(MAX)    NULL,
    [CreatedAt]     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]     DATETIME2        NULL,
    CONSTRAINT [FK_Agents_Businesses]  FOREIGN KEY ([BusinessId])  REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_Agents_AgentTypes]  FOREIGN KEY ([AgentTypeId]) REFERENCES [dbo].[AgentTypes]([AgentTypeId]),
    CONSTRAINT [UQ_Agents_BusinessName] UNIQUE ([BusinessId], [Name])
);
CREATE INDEX [IX_Agents_BusinessId] ON [dbo].[Agents] ([BusinessId]);

-- AgentPromptSections
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AgentPromptSections')
CREATE TABLE [dbo].[AgentPromptSections] (
    [AgentPromptSectionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_AgentPromptSections] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [AgentId]              UNIQUEIDENTIFIER NOT NULL,
    [Key]                  NVARCHAR(100)    NOT NULL,
    [Title]                NVARCHAR(200)    NOT NULL,
    [Content]              NVARCHAR(MAX)    NOT NULL,
    [InjectionPoint]       NVARCHAR(50)     NOT NULL DEFAULT 'before_instructions',
    [DisplayOrder]         INT              NOT NULL DEFAULT 0,
    [IsActive]             BIT              NOT NULL DEFAULT 1,
    [CreatedAt]            DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]            DATETIME2        NULL,
    CONSTRAINT [FK_AgentPromptSections_Agents] FOREIGN KEY ([AgentId]) REFERENCES [dbo].[Agents]([AgentId]) ON DELETE CASCADE,
    CONSTRAINT [UQ_AgentPromptSections_AgentKey] UNIQUE ([AgentId], [Key])
);
CREATE INDEX [IX_AgentPromptSections_AgentId] ON [dbo].[AgentPromptSections] ([AgentId]);

-- KnowledgeSources
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'KnowledgeSources')
CREATE TABLE [dbo].[KnowledgeSources] (
    [KnowledgeSourceId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_KnowledgeSources] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [BusinessId]        UNIQUEIDENTIFIER NOT NULL,
    [Type]              INT              NOT NULL DEFAULT 0,   -- KnowledgeSourceType enum
    [Name]              NVARCHAR(200)    NOT NULL,
    [ContentJson]       NVARCHAR(MAX)    NOT NULL,
    [IsActive]          BIT              NOT NULL DEFAULT 1,
    [CreatedAt]         DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]         DATETIME2        NULL,
    CONSTRAINT [FK_KnowledgeSources_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId])
);
CREATE INDEX [IX_KnowledgeSources_BusinessId] ON [dbo].[KnowledgeSources] ([BusinessId]);

-- AgentKnowledgeSources (junction)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AgentKnowledgeSources')
CREATE TABLE [dbo].[AgentKnowledgeSources] (
    [AgentKnowledgeSourceId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_AgentKnowledgeSources] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [AgentId]                UNIQUEIDENTIFIER NOT NULL,
    [KnowledgeSourceId]      UNIQUEIDENTIFIER NOT NULL,
    [AutoInject]             BIT              NOT NULL DEFAULT 0,
    [DisplayOrder]           INT              NOT NULL DEFAULT 0,
    CONSTRAINT [FK_AgentKnowledgeSources_Agents]          FOREIGN KEY ([AgentId])           REFERENCES [dbo].[Agents]([AgentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_AgentKnowledgeSources_KnowledgeSources] FOREIGN KEY ([KnowledgeSourceId]) REFERENCES [dbo].[KnowledgeSources]([KnowledgeSourceId]) ON DELETE CASCADE,
    CONSTRAINT [UQ_AgentKnowledgeSources] UNIQUE ([AgentId], [KnowledgeSourceId])
);

-- FlowDefinitions
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FlowDefinitions')
CREATE TABLE [dbo].[FlowDefinitions] (
    [FlowDefinitionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_FlowDefinitions] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [AgentId]          UNIQUEIDENTIFIER NOT NULL,
    [Name]             NVARCHAR(200)    NOT NULL,
    [Description]      NVARCHAR(500)    NULL,
    [DefinitionJson]   NVARCHAR(MAX)    NOT NULL,
    [Version]          NVARCHAR(20)     NOT NULL DEFAULT '1.0',
    [IsActive]         BIT              NOT NULL DEFAULT 1,
    [CreatedAt]        DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]        DATETIME2        NULL,
    CONSTRAINT [FK_FlowDefinitions_Agents] FOREIGN KEY ([AgentId]) REFERENCES [dbo].[Agents]([AgentId]) ON DELETE CASCADE
);
CREATE INDEX [IX_FlowDefinitions_AgentId] ON [dbo].[FlowDefinitions] ([AgentId]);
CREATE INDEX [IX_FlowDefinitions_AgentActive] ON [dbo].[FlowDefinitions] ([AgentId], [IsActive]);

-- FlowExecutionStates
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FlowExecutionStates')
CREATE TABLE [dbo].[FlowExecutionStates] (
    [FlowExecutionStateId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_FlowExecutionStates] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [BusinessId]           UNIQUEIDENTIFIER NOT NULL,
    [UserIdentifier]       NVARCHAR(100)    NOT NULL,
    [AgentId]              UNIQUEIDENTIFIER NOT NULL,
    [FlowDefinitionId]     UNIQUEIDENTIFIER NOT NULL,
    [CurrentNodeId]        NVARCHAR(100)    NOT NULL DEFAULT 'start',
    [IsWaitingForUser]     BIT              NOT NULL DEFAULT 0,
    [Owner]                NVARCHAR(20)     NOT NULL DEFAULT 'Bot',
    [VariablesJson]        NVARCHAR(MAX)    NOT NULL DEFAULT '{}',
    [FlagsJson]            NVARCHAR(MAX)    NOT NULL DEFAULT '{}',
    [ActionResultsJson]    NVARCHAR(MAX)    NOT NULL DEFAULT '{}',
    [TraceJson]            NVARCHAR(MAX)    NULL,
    [PreviousSessionJson]  NVARCHAR(MAX)    NULL,
    [Version]              INT              NOT NULL DEFAULT 1,
    [CreatedAt]            DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]            DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [SessionStartedAt]     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_FlowExecutionStates_Agents]          FOREIGN KEY ([AgentId])          REFERENCES [dbo].[Agents]([AgentId]),
    CONSTRAINT [FK_FlowExecutionStates_FlowDefinitions] FOREIGN KEY ([FlowDefinitionId]) REFERENCES [dbo].[FlowDefinitions]([FlowDefinitionId]),
    CONSTRAINT [UQ_FlowExecutionStates_Session] UNIQUE ([BusinessId], [UserIdentifier], [AgentId])
);
CREATE INDEX [IX_FlowExecutionStates_AgentId] ON [dbo].[FlowExecutionStates] ([AgentId]);

-- FlowTemplates
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FlowTemplates')
CREATE TABLE [dbo].[FlowTemplates] (
    [FlowTemplateId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_FlowTemplates] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Name]           NVARCHAR(200)    NOT NULL,
    [Description]    NVARCHAR(500)    NULL,
    [Category]       NVARCHAR(100)    NULL,
    [DefinitionJson] NVARCHAR(MAX)    NOT NULL,
    [IsActive]       BIT              NOT NULL DEFAULT 1,
    [CreatedAt]      DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]      DATETIME2        NULL
);

-- =============================================================================
-- SECTION 2: SEED DATA — MIMO'S BABY SPA
-- =============================================================================

-- Variables used throughout this seed
DECLARE @BusinessId     UNIQUEIDENTIFIER;
DECLARE @AgentTypeId    UNIQUEIDENTIFIER = NEWID();
DECLARE @AgentId        UNIQUEIDENTIFIER = NEWID();
DECLARE @KsCatalogId    UNIQUEIDENTIFIER = NEWID();
DECLARE @KsFaqId        UNIQUEIDENTIFIER = NEWID();
DECLARE @KsProfileId    UNIQUEIDENTIFIER = NEWID();
DECLARE @FlowDefId      UNIQUEIDENTIFIER = NEWID();

-- Get the Mimo's Baby Spa BusinessId (adjust name if different)
SELECT TOP 1 @BusinessId = BusinessId FROM [dbo].[Businesses] WHERE Name LIKE '%Mimo%' OR Name LIKE '%Baby Spa%';

IF @BusinessId IS NULL
BEGIN
    RAISERROR('Business "Mimo''s Baby Spa" not found. Please ensure the business exists before running this seed.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ── Agent Type ────────────────────────────────────────────────────────────────
INSERT INTO [dbo].[AgentTypes] ([AgentTypeId], [Name], [Description])
VALUES (@AgentTypeId, 'Vendedor', 'Agente de ventas y reservas — gestiona el proceso completo de agendamiento');

-- ── Agent ────────────────────────────────────────────────────────────────────
INSERT INTO [dbo].[Agents] ([AgentId], [BusinessId], [AgentTypeId], [Name], [Description], [IsActive], [SettingsJson])
VALUES (
    @AgentId,
    @BusinessId,
    @AgentTypeId,
    'Mimo Bot',
    'Agente principal de Mimo''s Baby Spa — reservas, pagos y atención al cliente',
    1,
    N'{
        "engine": {
            "maxNodesPerTurn": 25,
            "maxConversationHistoryMessages": 10,
            "extractionTemperature": 0.1,
            "responseTemperature": 0.7,
            "responseMaxTokens": 600,
            "classifyMaxTokens": 80
        },
        "messages": {
            "escalationMessage": "Entendido, te estoy conectando con una de nuestras asesoras. Un momento por favor 🙏",
            "errorMessage": "Disculpa, ocurrió un problema. ¿Puedes repetirlo?",
            "inactivityMessage": "¡Hola de nuevo! ¿En qué puedo ayudarte hoy? 😊"
        },
        "payment": {
            "requiresAnticipo": true,
            "anticipoPercentage": 50
        },
        "escalation": {
            "contacts": [],
            "notificationTemplate": "{{variables.customer_name}} necesita asistencia. Motivo: {{reason}}"
        }
    }'
);

-- ── Agent Prompt Sections ─────────────────────────────────────────────────────
INSERT INTO [dbo].[AgentPromptSections] ([AgentPromptSectionId], [AgentId], [Key], [Title], [Content], [InjectionPoint], [DisplayOrder])
VALUES
(NEWID(), @AgentId, 'identity', 'ROL E IDENTIDAD',
N'Eres Mimo, la asistente virtual de Mimo''s Baby Spa. Eres cálida, empática y profesional.
Tu misión es ayudar a los papás y mamás a agendar servicios de relajación y bienestar para sus bebés de la forma más fácil y placentera posible.
Siempre hablas en español, usas emojis con moderación y mantienes un tono conversacional y amigable.',
'system_header', 10),

(NEWID(), @AgentId, 'operating_rules', 'REGLAS DE OPERACIÓN',
N'- Responde SIEMPRE en español.
- Sé concisa pero completa — no hagas preguntas innecesarias.
- Si el usuario proporciona múltiples datos en un mensaje, úsalos todos sin preguntar de nuevo.
- Ante dudas sobre disponibilidad, siempre ofrece alternativas.
- Nunca inventes información sobre precios o disponibilidad que no tengas confirmada.
- Si el cliente se frustra o pide hablar con alguien, escala inmediatamente.',
'before_instructions', 20);

-- ── Knowledge Sources ─────────────────────────────────────────────────────────

-- Service Catalog
INSERT INTO [dbo].[KnowledgeSources] ([KnowledgeSourceId], [BusinessId], [Type], [Name], [ContentJson])
VALUES (
    @KsCatalogId,
    @BusinessId,
    0, -- ServiceCatalog
    'Catálogo de Servicios Mimo''s Baby Spa',
    N'[
        {
            "serviceName": "Baby Spa Básico",
            "description": "Sesión de relajación para bebés que incluye masaje suave, baño de burbujas y musicoterapia.",
            "duration": "45 min",
            "price": "120000",
            "currency": "COP",
            "minAge": 1,
            "maxAge": 24,
            "addOns": [
                { "name": "Foto recuerdo digital", "price": "25000" },
                { "name": "Kit aromaterapia", "price": "35000" }
            ]
        },
        {
            "serviceName": "Baby Spa Premium",
            "description": "Experiencia premium: masaje completo, baño de burbujas con esencias naturales, musicoterapia y meditación guiada para mamá.",
            "duration": "75 min",
            "price": "220000",
            "currency": "COP",
            "minAge": 1,
            "maxAge": 36,
            "addOns": [
                { "name": "Foto recuerdo digital", "price": "25000" },
                { "name": "Kit aromaterapia premium", "price": "55000" },
                { "name": "Video sesión", "price": "45000" }
            ]
        },
        {
            "serviceName": "Baby Spa Duo",
            "description": "Sesión especial para gemelos o dos bebés de la misma familia. Incluye todo lo del Básico para cada bebé.",
            "duration": "60 min",
            "price": "200000",
            "currency": "COP",
            "minAge": 1,
            "maxAge": 24,
            "addOns": [
                { "name": "Foto recuerdo digital", "price": "25000" }
            ]
        }
    ]'
);

-- FAQ
INSERT INTO [dbo].[KnowledgeSources] ([KnowledgeSourceId], [BusinessId], [Type], [Name], [ContentJson])
VALUES (
    @KsFaqId,
    @BusinessId,
    3, -- FAQ
    'Preguntas Frecuentes Mimo''s Baby Spa',
    N'[
        {
            "question": "¿Desde qué edad pueden venir los bebés?",
            "answer": "Recibimos bebés desde el primer mes de vida. Para el Spa Básico y Duo hasta los 24 meses, para el Premium hasta los 36 meses."
        },
        {
            "question": "¿Cuánto tiempo dura la sesión?",
            "answer": "El Baby Spa Básico dura 45 minutos, el Premium 75 minutos y el Duo 60 minutos. Te pedimos llegar 5 minutos antes."
        },
        {
            "question": "¿Cómo funciona el anticipo?",
            "answer": "Para confirmar tu reserva pedimos un anticipo del 50% a través de un link de pago seguro. El resto lo pagas el día de la sesión."
        },
        {
            "question": "¿Puedo cambiar mi cita?",
            "answer": "Sí, puedes reagendar tu cita con al menos 24 horas de anticipación sin costo adicional."
        },
        {
            "question": "¿Qué debo llevar?",
            "answer": "Solo trae a tu bebé con ropa cómoda. Nosotros ponemos toallas, pañales y todo lo necesario para la sesión."
        },
        {
            "question": "¿Dónde están ubicados?",
            "answer": "Estamos en Bogotá. La dirección exacta te la enviamos al confirmar tu reserva."
        }
    ]'
);

-- Business Profile
INSERT INTO [dbo].[KnowledgeSources] ([KnowledgeSourceId], [BusinessId], [Type], [Name], [ContentJson])
VALUES (
    @KsProfileId,
    @BusinessId,
    5, -- BusinessProfile
    'Perfil Mimo''s Baby Spa',
    N'{
        "name": "Mimo''s Baby Spa",
        "description": "Centro especializado en bienestar y relajación para bebés y sus mamás.",
        "phone": "Disponible en chat",
        "hours": "Lunes a Sábado 9:00am - 6:00pm",
        "location": "Bogotá, Colombia",
        "instagram": "@mimosbabyspa"
    }'
);

-- ── Link Knowledge Sources to Agent ──────────────────────────────────────────
INSERT INTO [dbo].[AgentKnowledgeSources] ([AgentKnowledgeSourceId], [AgentId], [KnowledgeSourceId], [AutoInject], [DisplayOrder])
VALUES
(NEWID(), @AgentId, @KsCatalogId, 0, 10),
(NEWID(), @AgentId, @KsFaqId,     0, 20),
(NEWID(), @AgentId, @KsProfileId, 1, 5);  -- Auto-inject business profile

-- ── Flow Definition — Mimo's Baby Spa ─────────────────────────────────────────
INSERT INTO [dbo].[FlowDefinitions] ([FlowDefinitionId], [AgentId], [Name], [Description], [Version], [IsActive], [DefinitionJson])
VALUES (
    @FlowDefId,
    @AgentId,
    'Flujo Reservas Mimo''s Baby Spa',
    'Flujo principal de reservas: recopilación de datos, verificación de disponibilidad, confirmación y pago.',
    '1.0',
    1,
    N'{
  "variables": [
    {
      "key": "customer_name", "label": "Nombre del cliente", "dataType": "String",
      "required": true, "group": "identity", "displayOrder": 1, "showInSummary": true,
      "isSystemManaged": false,
      "extractionHint": "Nombre completo del cliente. Mínimo 2 caracteres.",
      "validation": { "minLength": 2 }
    },
    {
      "key": "email", "label": "Email", "dataType": "Email",
      "required": true, "group": "identity", "displayOrder": 2, "showInSummary": true,
      "isSystemManaged": false,
      "extractionHint": "Correo electrónico válido",
      "validation": { "pattern": "^[\\w.-]+@[\\w.-]+\\.\\w+$" }
    },
    {
      "key": "phone", "label": "Teléfono", "dataType": "Phone",
      "required": false, "group": "identity", "displayOrder": 3, "showInSummary": true,
      "isSystemManaged": true
    },
    {
      "key": "service", "label": "Servicio", "dataType": "Enum",
      "required": true, "group": "transaction", "displayOrder": 4, "showInSummary": true,
      "isSystemManaged": false,
      "extractionHint": "Nombre EXACTO del servicio del catálogo: Baby Spa Básico, Baby Spa Premium o Baby Spa Duo",
      "applyGuards": [{ "type": "skipWhenIntention", "value": "is_information_query" }],
      "onChange": {
        "resetVariables": ["desired_time", "available_time_slots"],
        "resetFlags": ["availability_confirmed", "reservation_confirmed", "add_ons_offered", "confirmation_summary_presented"],
        "gotoNode": "offer_addons"
      }
    },
    {
      "key": "desired_date", "label": "Fecha deseada", "dataType": "Date",
      "required": true, "group": "transaction", "displayOrder": 5, "showInSummary": true,
      "isSystemManaged": false,
      "extractionHint": "Fecha ISO. ''mañana''=fecha mañana, ''el viernes''=próximo viernes.",
      "validation": { "minDate": "today" },
      "applyGuards": [{ "type": "skipWhenIntention", "value": "is_information_query" }],
      "onChange": {
        "resetVariables": ["desired_time", "available_time_slots"],
        "resetFlags": ["availability_confirmed", "reservation_confirmed", "confirmation_summary_presented"],
        "gotoNode": "collect_date"
      }
    },
    {
      "key": "desired_time", "label": "Hora deseada", "dataType": "Time",
      "required": true, "group": "transaction", "displayOrder": 6, "showInSummary": true,
      "isSystemManaged": false,
      "extractionHint": "Hora en formato 24h. ''9am''=''09:00'', ''2pm''=''14:00''",
      "applyGuards": [{ "type": "skipWhenIntention", "value": "is_information_query" }],
      "onChange": {
        "resetFlags": ["availability_confirmed", "reservation_confirmed", "confirmation_summary_presented"],
        "gotoNode": "check_availability"
      }
    },
    {
      "key": "baby_name", "label": "Nombre del bebé", "dataType": "String",
      "required": true, "group": "business", "displayOrder": 7, "showInSummary": true,
      "isSystemManaged": false, "extractionHint": "Nombre del bebé",
      "validation": { "minLength": 2 }
    },
    {
      "key": "baby_age", "label": "Edad del bebé (meses)", "dataType": "Number",
      "required": true, "group": "business", "displayOrder": 8, "showInSummary": true,
      "isSystemManaged": false,
      "extractionHint": "Edad en meses. ''1 año''=12, ''6 meses''=6",
      "validation": { "min": 1, "max": 120 }
    },
    {
      "key": "available_time_slots", "label": "Horarios disponibles", "dataType": "String",
      "required": false, "group": "system", "isSystemManaged": true, "showInSummary": false
    },
    {
      "key": "reservation_id", "label": "ID Reserva", "dataType": "String",
      "required": false, "group": "system", "isSystemManaged": true, "showInSummary": false
    },
    {
      "key": "payment_reference_id", "label": "Referencia de pago", "dataType": "String",
      "required": false, "group": "system", "isSystemManaged": true, "showInSummary": false
    },
    {
      "key": "payment_link_url", "label": "Link de pago", "dataType": "String",
      "required": false, "group": "system", "isSystemManaged": true, "showInSummary": false
    },
    {
      "key": "anticipo_amount", "label": "Anticipo requerido", "dataType": "String",
      "required": false, "group": "system", "isSystemManaged": true, "showInSummary": false
    }
  ],

  "intentions": [
    {
      "key": "user_wants_human", "description": "El usuario pide hablar con una persona real",
      "detectionExamples": ["quiero hablar con alguien", "un asesor", "un humano", "quiero una persona"],
      "regexFallback": "(?i)(hablar con|asesor|humano|persona real|agente|operador|ayuda de verdad)",
      "priority": 1, "alwaysDetect": true,
      "behavior": { "action": "escalate" }
    },
    {
      "key": "user_wants_cancel", "description": "El usuario quiere cancelar el proceso",
      "detectionExamples": ["cancelar", "no quiero", "déjalo", "olvídalo"],
      "regexFallback": "(?i)(cancelar|no quiero|déjalo|olvíd|ya no quiero)",
      "priority": 2, "alwaysDetect": true,
      "behavior": { "action": "goto_node", "targetNodeId": "cancel_response" }
    },
    {
      "key": "user_wants_reschedule", "description": "El usuario quiere cambiar o mover una cita existente",
      "detectionExamples": ["cambiar mi cita", "reagendar", "mover mi reserva", "otro día"],
      "regexFallback": "(?i)(reagendar|cambiar.*cita|mover.*reserva|otro día|cambiar.*fecha)",
      "priority": 3, "alwaysDetect": true,
      "behavior": { "action": "goto_node", "targetNodeId": "reschedule_setup" }
    },
    {
      "key": "user_wants_hold", "description": "El usuario quiere pausar su reserva",
      "detectionExamples": ["ponlo en espera", "no puedo ir pero no canceles", "pausar"],
      "regexFallback": "(?i)(espera|pausar|no puedo.*ir|suspender)",
      "priority": 4, "alwaysDetect": true,
      "behavior": { "action": "goto_node", "targetNodeId": "hold_handler" }
    },
    {
      "key": "is_information_query", "description": "El usuario solo pide información sin intención de reservar",
      "detectionExamples": ["qué servicios tienen", "cuánto cuesta", "horarios", "información"],
      "priority": 10, "alwaysDetect": true,
      "behavior": { "action": "none" }
    }
  ],

  "sessionConfig": {
    "abandonmentTimeoutHours": 8.0,
    "resetOnFlowCompletion": true,
    "completionFlags": ["reservation_created"],
    "persistentVariables": ["customer_name", "email", "phone", "baby_name", "baby_age"],
    "transactionalVariables": ["service", "desired_date", "desired_time", "available_time_slots", "reservation_id", "payment_reference_id", "payment_link_url", "anticipo_amount"]
  },

  "engineSettings": {
    "maxNodesPerTurn": 25,
    "maxConversationHistoryMessages": 6,
    "extractionTemperature": 0.1,
    "extractionMaxTokens": 600,
    "extractionMinConfidence": 0.6,
    "responseTemperature": 0.7,
    "responseMaxTokens": 600,
    "classifyMaxTokens": 80,
    "degradedTurnsBeforeEscalation": 2
  },

  "extractionInstructions": "Contexto temporal:\n- Hoy: {{runtime.today}} ({{runtime.day_of_week}})\n- Mañana: {{runtime.tomorrow}}\n- Pasado mañana: {{runtime.day_after_tomorrow}}\n- Hora actual: {{runtime.current_time}}\n\nReglas de fechas (mapeo obligatorio):\n- \"hoy\" → {{runtime.today}}\n- \"mañana\" → {{runtime.tomorrow}}\n- \"pasado mañana\" → {{runtime.day_after_tomorrow}}\n- Días de semana (\"el viernes\", \"el próximo lunes\") → próxima ocurrencia futura desde hoy.\n- Número de día solo (\"el 15\", \"para el 29\") → ese día en el mes actual si aún no pasó, o en el mes siguiente si ya pasó.\n- Si el usuario pide disponibilidad u horarios mencionando una fecha (incluso \"¿y para mañana?\", \"¿tienen para el viernes?\") → extraer la fecha correspondiente.\n\nReglas de horas (mapeo obligatorio):\n- Convertir siempre a HH:MM formato 24h.\n- \"9am\" → \"09:00\", \"2pm\" → \"14:00\", \"mediodía\" → \"12:00\", \"a las 3\" → \"15:00\" si es por la tarde según contexto.\n\nReglas de resolución contextual:\n- Valor directo: el usuario proporciona el dato explícitamente (\"quiero el Baby Spa Premium\", \"a las 10\", \"el viernes\").\n- Aceptación por referencia: el asistente mostró opciones y el usuario acepta (\"sí\", \"ese\", \"la primera\", \"esa\", \"está bien\") → resolver al valor EXACTO del catálogo usando el historial de conversación.\n- Nombre parcial o variación: resolver al nombre exacto del catálogo (\"el básico\" → \"Baby Spa Básico\", \"el premium\" → \"Baby Spa Premium\").\n- Si hay varios candidatos o la referencia es ambigua → marcar como ambigüedad tipo \"referential\", confidence < 0.6, no extraer.\n\nConfianza esperada:\n- Dato explícito e inequívoco: 0.95\n- Referencia resuelta con certeza desde historial: 0.90\n- Referencia ambigua o múltiples candidatos: 0.65–0.70\n- Solo incluir campos con confidence >= 0.6",

  "nodes": [
    { "id": "start", "type": 0, "label": "Inicio", "config": {} },

    {
      "id": "detect_intent", "type": 3, "label": "¿Qué quiere?",
      "config": {
        "prompt": "¿El usuario quiere hacer una reserva, pedir información sobre servicios/precios, o es solo un saludo/mensaje genérico?",
        "outputVariable": "_intent",
        "possibleValues": ["reservation", "information", "other"]
      }
    },

    {
      "id": "info_response", "type": 4, "label": "Responder info",
      "config": {
        "responseMode": "llm",
        "waitForUser": true,
        "instructions": "Responde la consulta del cliente usando el catálogo de servicios y FAQ. Sé informativa y amigable. Si detectas interés en reservar, sugiere suavemente agendar.",
        "knowledgeSourceIds": [' + N'"' + CAST(@KsCatalogId AS NVARCHAR(36)) + N'"' + N', ' + N'"' + CAST(@KsFaqId AS NVARCHAR(36)) + N'"' + N']
      }
    },

    {
      "id": "collect_service", "type": 1, "label": "Elegir servicio",
      "config": {
        "fields": ["service"],
        "instructions": "Ayuda al cliente a elegir un servicio del catálogo. Si es primera vez, saluda cálidamente. Si hay sesión previa menciona el servicio anterior: {{previous_session.service}}.",
        "knowledgeSourceIds": [' + N'"' + CAST(@KsCatalogId AS NVARCHAR(36)) + N'"' + N']
      }
    },

    {
      "id": "offer_addons", "type": 4, "label": "Ofrecer extras",
      "config": {
        "executeWhen": { "type": "FlagIsFalse", "parameters": { "flag": "add_ons_offered" } },
        "setFlags": { "add_ons_offered": true },
        "responseMode": "llm",
        "waitForUser": true,
        "instructions": "El cliente eligió {{variables.service}}. De forma natural y sin presionar, menciona los servicios extra disponibles y pregunta si desea agregar alguno.",
        "knowledgeSourceIds": [' + N'"' + CAST(@KsCatalogId AS NVARCHAR(36)) + N'"' + N']
      }
    },

    {
      "id": "collect_date", "type": 1, "label": "Recopilar fecha y hora",
      "config": {
        "fields": ["desired_date", "desired_time"],
        "instructions": "Pregunta cuándo le gustaría agendar. Pide fecha y hora en el mismo mensaje si es posible."
      }
    },

    {
      "id": "check_availability", "type": 2, "label": "Verificar disponibilidad",
      "config": {
        "action_type": "check_availability",
        "input_mapping": {
          "item": "{{variables.service}}",
          "date": "{{variables.desired_date}}",
          "time": "{{variables.desired_time}}"
        },
        "output_mapping": {
          "available_time_slots": "slots",
          "flag:availability_confirmed": "has_exact_match"
        }
      }
    },

    {
      "id": "show_alternatives", "type": 4, "label": "Mostrar alternativas",
      "config": {
        "responseMode": "llm",
        "waitForUser": true,
        "instructions": "No hay disponibilidad exacta para la fecha/hora solicitada. Horarios disponibles: {{variables.available_time_slots}}. Presenta las alternativas de forma amigable y pide al cliente que elija una."
      }
    },

    {
      "id": "collect_identity", "type": 1, "label": "Datos del cliente",
      "config": {
        "fields": ["customer_name", "email", "baby_name", "baby_age"],
        "instructions": "Necesitamos algunos datos para confirmar la reserva. Pídelos de forma conversacional y cálida."
      }
    },

    {
      "id": "show_confirmation", "type": 4, "label": "Mostrar resumen",
      "config": {
        "setFlags": { "confirmation_summary_presented": true },
        "responseMode": "template",
        "waitForUser": true,
        "instructions": "📋 *Resumen de tu reserva:*\n\n🎯 Servicio: {{variables.service}}\n📅 Fecha: {{variables.desired_date}}\n🕐 Hora: {{variables.desired_time}}\n👤 {{variables.customer_name}}\n📧 {{variables.email}}\n👶 {{variables.baby_name}} ({{variables.baby_age}} meses)\n\n¿Confirmas estos datos? ✅"
      }
    },

    {
      "id": "detect_confirmation", "type": 3, "label": "¿Confirma reserva?",
      "config": {
        "prompt": "¿El cliente confirma la reserva tal como está, quiere cambiar algún dato, o quiere cancelar?",
        "outputVariable": "_confirm",
        "possibleValues": ["confirmed", "wants_changes", "cancel"]
      }
    },

    {
      "id": "reschedule_reservation", "type": 2, "label": "Reagendar cita",
      "config": {
        "executeWhen": { "type": "FlagIsTrue", "parameters": { "flag": "is_rescheduling" } },
        "action_type": "reschedule",
        "input_mapping": {
          "reservation_id": "{{variables.reservation_id}}",
          "date": "{{variables.desired_date}}",
          "time": "{{variables.desired_time}}"
        },
        "output_mapping": { "flag:reservation_created": "success" }
      }
    },

    {
      "id": "generate_payment_link", "type": 2, "label": "Generar link de pago",
      "config": {
        "executeWhen": { "type": "FlagIsFalse", "parameters": { "flag": "is_rescheduling" } },
        "action_type": "generate_payment_link",
        "input_mapping": {
          "item": "{{variables.service}}",
          "attributes": "{{variables_group:business}}"
        },
        "output_mapping": {
          "payment_link_url": "link_url",
          "payment_reference_id": "reference_id",
          "anticipo_amount_cents": "anticipo_amount_cents"
        },
        "payment": {
          "requiresAnticipo": true,
          "anticipoPercentage": 50,
          "currency": "COP",
          "expirationMinutes": 1440
        }
      }
    },

    {
      "id": "wait_payment", "type": 5, "label": "Esperar pago",
      "config": {
        "event_type": "payment_confirmed",
        "waitingMessage": "📋 *Resumen de tu reserva:*\n\n{{collected_data}}\n\n💰 Para confirmar tu reserva se requiere un anticipo de {{variables.anticipo_amount}}:\n\n💳 {{variables.payment_link_url}}\n\nUna vez realizado el pago, tu reserva quedará confirmada automáticamente 🎉",
        "localIntentions": [
          {
            "key": "user_says_paid",
            "description": "El usuario afirma que ya realizó el pago",
            "detectionExamples": ["ya pagué", "listo el pago", "ya transferí", "hice el pago"],
            "behavior": { "action": "advance_port", "targetPort": "user_claims_done" }
          },
          {
            "key": "user_wants_new_link",
            "description": "El usuario pide un nuevo link de pago",
            "detectionExamples": ["otro link", "no funciona el link", "mándame otro link"],
            "behavior": { "action": "advance_port", "targetPort": "new_link_requested" }
          }
        ]
      }
    },

    {
      "id": "verify_payment", "type": 2, "label": "Verificar pago",
      "config": {
        "action_type": "verify_payment",
        "input_mapping": { "reference_id": "{{variables.payment_reference_id}}" },
        "output_mapping": { "flag:payment_confirmed": "confirmed" }
      }
    },

    {
      "id": "payment_not_found", "type": 4, "label": "Pago no encontrado",
      "config": {
        "responseMode": "template",
        "waitForUser": true,
        "instructions": "Aún no encontramos tu pago registrado. Puede tomar unos minutos en procesarse. Puedes intentar de nuevo o usar este link:\n\n💳 {{variables.payment_link_url}}"
      }
    },

    {
      "id": "create_reservation", "type": 2, "label": "Crear reserva",
      "config": {
        "action_type": "create_reservation",
        "input_mapping": {
          "item": "{{variables.service}}",
          "date": "{{variables.desired_date}}",
          "time": "{{variables.desired_time}}",
          "customer_name": "{{variables.customer_name}}",
          "customer_email": "{{variables.email}}",
          "customer_phone": "{{variables.phone}}",
          "attributes": "{{variables_group:business}}"
        },
        "output_mapping": {
          "reservation_id": "reservation_id",
          "flag:reservation_created": "success"
        }
      }
    },

    {
      "id": "success_response", "type": 4, "label": "Confirmación exitosa",
      "config": {
        "responseMode": "template",
        "waitForUser": false,
        "instructions": "🎉 *¡Reserva confirmada!*\n\n📋 *Número de reserva:* #{{variables.reservation_id}}\n\n{{collected_data}}\n\n¡Te esperamos con mucho cariño en Mimo''s Baby Spa! 💕\nSi necesitas cambiar tu cita o tienes alguna pregunta, escríbenos con gusto."
      }
    },

    {
      "id": "cancel_response", "type": 4, "label": "Respuesta de cancelación",
      "config": {
        "setVariables": {
          "service": null, "desired_date": null, "desired_time": null,
          "available_time_slots": null, "reservation_id": null,
          "payment_reference_id": null, "payment_link_url": null, "anticipo_amount": null
        },
        "setFlags": {
          "availability_confirmed": false, "reservation_confirmed": false,
          "add_ons_offered": false, "confirmation_summary_presented": false,
          "is_rescheduling": false
        },
        "responseMode": "llm",
        "waitForUser": true,
        "instructions": "El cliente canceló el proceso. Agradece su tiempo, muéstrate disponible para cuando quiera agendar en el futuro."
      }
    },

    {
      "id": "reschedule_setup", "type": 2, "label": "Configurar reagendamiento",
      "config": {
        "action_type": "setup_reschedule",
        "output_mapping": {
          "reservation_id": "original_reservation_id",
          "service": "original_service",
          "flag:is_rescheduling": "success"
        }
      }
    },

    {
      "id": "hold_handler", "type": 2, "label": "Pausar reserva",
      "config": {
        "action_type": "suspend",
        "input_mapping": { "reservation_id": "{{variables.reservation_id}}" },
        "onSuccessTemplate": "Tu reserva está en pausa. Cuando quieras reagendar, escríbenos y con gusto te ayudamos 🙌",
        "onFailureTemplate": "No encontré una reserva activa para pausar. ¿Quieres hablar con una asesora?"
      }
    },

    {
      "id": "escalate", "type": 6, "label": "Escalar a humano",
      "config": { "reason": "Escalación automática — flujo de reservas" }
    },

    { "id": "end", "type": 7, "label": "Fin del flujo", "config": {} }
  ],

  "edges": [
    { "id": "e01", "sourceNodeId": "start",                 "targetNodeId": "detect_intent" },
    { "id": "e02", "sourceNodeId": "detect_intent",         "targetNodeId": "collect_service",        "portId": "reservation" },
    { "id": "e03", "sourceNodeId": "detect_intent",         "targetNodeId": "info_response",          "portId": "information" },
    { "id": "e04", "sourceNodeId": "detect_intent",         "targetNodeId": "collect_service",        "portId": "other" },
    { "id": "e05", "sourceNodeId": "info_response",         "targetNodeId": "detect_intent" },
    { "id": "e06", "sourceNodeId": "collect_service",       "targetNodeId": "offer_addons" },
    { "id": "e07", "sourceNodeId": "offer_addons",          "targetNodeId": "collect_date" },
    { "id": "e07s","sourceNodeId": "offer_addons",          "targetNodeId": "collect_date",           "portId": "skipped" },
    { "id": "e08", "sourceNodeId": "collect_date",          "targetNodeId": "check_availability" },
    { "id": "e09", "sourceNodeId": "check_availability",    "targetNodeId": "collect_identity",       "portId": "success" },
    { "id": "e10", "sourceNodeId": "check_availability",    "targetNodeId": "show_alternatives",      "portId": "failure" },
    { "id": "e11", "sourceNodeId": "show_alternatives",     "targetNodeId": "collect_date" },
    { "id": "e12", "sourceNodeId": "collect_identity",      "targetNodeId": "generate_payment_link" },
    { "id": "e13", "sourceNodeId": "show_confirmation",     "targetNodeId": "detect_confirmation" },
    { "id": "e14", "sourceNodeId": "detect_confirmation",   "targetNodeId": "reschedule_reservation", "portId": "confirmed" },
    { "id": "e15", "sourceNodeId": "detect_confirmation",   "targetNodeId": "collect_service",        "portId": "wants_changes" },
    { "id": "e16", "sourceNodeId": "detect_confirmation",   "targetNodeId": "cancel_response",        "portId": "cancel" },
    { "id": "e17", "sourceNodeId": "reschedule_reservation","targetNodeId": "success_response",       "portId": "success" },
    { "id": "e18", "sourceNodeId": "reschedule_reservation","targetNodeId": "create_reservation",     "portId": "skipped" },
    { "id": "e19", "sourceNodeId": "reschedule_reservation","targetNodeId": "escalate",               "portId": "failure" },
    { "id": "e20", "sourceNodeId": "generate_payment_link", "targetNodeId": "wait_payment",           "portId": "success" },
    { "id": "e21", "sourceNodeId": "generate_payment_link", "targetNodeId": "show_confirmation",      "portId": "not_required" },
    { "id": "e21s","sourceNodeId": "generate_payment_link", "targetNodeId": "show_confirmation",      "portId": "skipped" },
    { "id": "e22", "sourceNodeId": "generate_payment_link", "targetNodeId": "escalate",               "portId": "failure" },
    { "id": "e23", "sourceNodeId": "wait_payment",          "targetNodeId": "create_reservation",     "portId": "received" },
    { "id": "e24", "sourceNodeId": "wait_payment",          "targetNodeId": "verify_payment",         "portId": "user_claims_done" },
    { "id": "e25", "sourceNodeId": "wait_payment",          "targetNodeId": "generate_payment_link",  "portId": "new_link_requested" },
    { "id": "e26", "sourceNodeId": "verify_payment",        "targetNodeId": "create_reservation",     "portId": "success" },
    { "id": "e27", "sourceNodeId": "verify_payment",        "targetNodeId": "payment_not_found",      "portId": "failure" },
    { "id": "e28", "sourceNodeId": "payment_not_found",     "targetNodeId": "wait_payment" },
    { "id": "e29", "sourceNodeId": "create_reservation",    "targetNodeId": "success_response",       "portId": "success" },
    { "id": "e30", "sourceNodeId": "create_reservation",    "targetNodeId": "escalate",               "portId": "failure" },
    { "id": "e31", "sourceNodeId": "success_response",      "targetNodeId": "end" },
    { "id": "e32", "sourceNodeId": "cancel_response",       "targetNodeId": "start" },
    { "id": "e33", "sourceNodeId": "reschedule_setup",      "targetNodeId": "collect_date",           "portId": "success" },
    { "id": "e34", "sourceNodeId": "reschedule_setup",      "targetNodeId": "escalate",               "portId": "failure" },
    { "id": "e35", "sourceNodeId": "hold_handler",          "targetNodeId": "end",                    "portId": "success" },
    { "id": "e36", "sourceNodeId": "hold_handler",          "targetNodeId": "escalate",               "portId": "failure" },
    { "id": "e37", "sourceNodeId": "escalate",              "targetNodeId": "end" }
  ]
}'
);

-- AgentId linking: see script 014_AgentIdOnWhatsAppNumber.sql.
-- It adds AgentId to BusinessWhatsAppNumbers and links the Mimo Bot agent.

-- =============================================================================
COMMIT TRANSACTION;

PRINT 'Migration 009 completed successfully.';
PRINT 'AgentId: ' + CAST(@AgentId AS NVARCHAR(36));
PRINT 'FlowDefinitionId: ' + CAST(@FlowDefId AS NVARCHAR(36));
PRINT 'KnowledgeSource (Catalog): ' + CAST(@KsCatalogId AS NVARCHAR(36));
PRINT 'KnowledgeSource (FAQ): ' + CAST(@KsFaqId AS NVARCHAR(36));
GO
