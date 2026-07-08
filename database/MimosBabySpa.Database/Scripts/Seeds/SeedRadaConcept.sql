-- =============================================================================
-- SeedRadaConcept.sql
--
-- Crea/actualiza el negocio Rada Concept, su catalogo de servicios y el agente
-- de agendamiento. Idempotente.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @TenantId        UNIQUEIDENTIFIER = 'AADA0000-0000-0000-0000-000000000000';
DECLARE @BusinessId      UNIQUEIDENTIFIER = 'AADA0000-0000-0000-0000-000000000001';
DECLARE @AgentId         UNIQUEIDENTIFIER = 'AADA0000-0000-0000-0000-000000000002';
DECLARE @EmployeeId      UNIQUEIDENTIFIER = 'AADA0000-0000-0000-0000-000000000003';
DECLARE @CategoryId      UNIQUEIDENTIFIER = 'AADA0000-0000-0000-0000-000000000010';
DECLARE @AgentTypeId     UNIQUEIDENTIFIER;

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.Tenants (TenantId, Name, Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'Rada Concept', N'admin@radaconcept.co', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Tenants
    SET Name = N'Rada Concept',
        Email = N'admin@radaconcept.co',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE TenantId = @TenantId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    INSERT INTO dbo.Businesses
        (BusinessId, TenantId, Name, Description, Address, Phone, Email, Website, IsActive, CreatedAt)
    VALUES
        (@BusinessId, @TenantId, N'Rada Concept',
         N'Estudio de diseno arquitectonico, interiorismo, mobiliario, remodelaciones y espacios comerciales.',
         N'Riohacha, La Guajira', N'+573007047440', N'admin@radaconcept.co', N'', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Businesses
    SET TenantId = @TenantId,
        Name = N'Rada Concept',
        Description = N'Estudio de diseno arquitectonico, interiorismo, mobiliario, remodelaciones y espacios comerciales.',
        Address = N'Riohacha, La Guajira',
        Phone = N'+573007047440',
        Email = N'admin@radaconcept.co',
        Website = N'',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories WHERE ServiceCategoryId = @CategoryId)
BEGIN
    INSERT INTO dbo.ServiceCategories
        (ServiceCategoryId, BusinessId, Name, Description, DisplayOrder, IsActive, CreatedAt)
    VALUES
        (@CategoryId, @BusinessId, N'Servicios de diseno',
         N'Servicios de arquitectura, interiorismo, mobiliario, remodelacion y espacios comerciales.',
         1, 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.ServiceCategories
    SET BusinessId = @BusinessId,
        Name = N'Servicios de diseno',
        Description = N'Servicios de arquitectura, interiorismo, mobiliario, remodelacion y espacios comerciales.',
        DisplayOrder = 1,
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE ServiceCategoryId = @CategoryId;
END

DECLARE @Services TABLE
(
    ServiceId UNIQUEIDENTIFIER NOT NULL,
    ServiceName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NOT NULL,
    DisplayOrder INT NOT NULL
);

INSERT INTO @Services (ServiceId, ServiceName, Description, DisplayOrder)
VALUES
('AADA0000-0000-0000-0000-000000000101', N'Diseno Arquitectonico',
 N'Creacion de espacios desde cero con vision estetica y funcional. Incluye diseno de viviendas, fachadas de alto impacto, distribucion inteligente y conceptualizacion personalizada.',
 1),
('AADA0000-0000-0000-0000-000000000102', N'Diseno Interior',
 N'Transformacion de espacios en experiencias. Incluye diseno de salas, cocinas y habitaciones, seleccion de materiales y acabados, y conceptos modernos y elegantes.',
 2),
('AADA0000-0000-0000-0000-000000000103', N'Mobiliario Arquitectonico',
 N'Diseno de piezas unicas que elevan cada espacio. Incluye cocinas integrales, centros de entretenimiento, closets y muebles a medida.',
 3),
('AADA0000-0000-0000-0000-000000000104', N'Remodelaciones',
 N'Reinvencion de espacios con nueva identidad. Incluye rediseno interior y exterior, y modernizacion de espacios.',
 4),
('AADA0000-0000-0000-0000-000000000105', N'Asesoria en Diseno',
 N'Acompanamiento para tomar decisiones clave. Incluye distribucion, materiales y acompanamiento profesional.',
 5),
('AADA0000-0000-0000-0000-000000000106', N'Espacios Comerciales',
 N'Creacion de lugares que venden y enamoran. Incluye showrooms, locales comerciales y experiencia de marca.',
 6);

MERGE dbo.Services AS target
USING @Services AS source
   ON target.BusinessId = @BusinessId
  AND target.ServiceName = source.ServiceName
WHEN MATCHED THEN
    UPDATE SET
        Description = source.Description,
        DurationMinutes = 60,
        Price = 0,
        IncludeInCheckoutTotal = 0,
        CategoryId = @CategoryId,
        Tier = 0,
        ServiceType = 0,
        FulfillmentKind = 0,
        FixedScheduleLabel = NULL,
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (ServiceId, BusinessId, ServiceName, Description, DurationMinutes, Price,
            IncludeInCheckoutTotal, CategoryId, Tier, ServiceType, FulfillmentKind,
            FixedScheduleLabel, IsActive, CreatedAt)
    VALUES (source.ServiceId, @BusinessId, source.ServiceName, source.Description, 60, 0,
            0, @CategoryId, 0, 0, 0, NULL, 1, GETUTCDATE());

UPDATE s
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
FROM dbo.Services s
WHERE s.BusinessId = @BusinessId
  AND NOT EXISTS (SELECT 1 FROM @Services src WHERE src.ServiceName = s.ServiceName);

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeId = @EmployeeId)
BEGIN
    INSERT INTO dbo.Employees (EmployeeId, BusinessId, Name, IsActive, CreatedAt)
    VALUES (@EmployeeId, @BusinessId, N'Equipo Rada Concept', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Employees
    SET BusinessId = @BusinessId,
        Name = N'Equipo Rada Concept',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE EmployeeId = @EmployeeId;
END

INSERT INTO dbo.EmployeeServices (EmployeeServiceId, EmployeeId, ServiceId, CreatedAt)
SELECT NEWID(), @EmployeeId, s.ServiceId, GETUTCDATE()
FROM dbo.Services s
WHERE s.BusinessId = @BusinessId
  AND s.IsActive = 1
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.EmployeeServices es
      WHERE es.EmployeeId = @EmployeeId
        AND es.ServiceId = s.ServiceId
  );

SELECT @AgentTypeId = AgentTypeId FROM dbo.AgentTypes WHERE Name = N'Vendedor';

IF @AgentTypeId IS NULL
BEGIN
    SET @AgentTypeId = NEWID();
    INSERT INTO dbo.AgentTypes (AgentTypeId, Name, Description, IsActive)
    VALUES (@AgentTypeId, N'Vendedor', N'Agente de ventas y agendamiento.', 1);
END

DECLARE @SystemPrompt NVARCHAR(MAX) = N'';

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.68,
  "maxToolIterations": 8,
  "historyWindowSize": 24,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "Eres el asistente comercial de Rada Concept por WhatsApp. Atiendes en espanol con tono cercano, elegante y profesional. Ayudas a entender el servicio adecuado y guias hacia una cita de asesoria sin presionar.\n\nResponde claro y breve. Usa listas cortas para explicar servicios, opciones, horarios o resumen de cita.",
  "policies": "## MARCA\n\n- Rada Concept crea espacios funcionales y esteticos para vivienda, mobiliario, remodelaciones y proyectos comerciales.\n- La cotizacion se define despues de entender el proyecto en una asesoria.\n\n## APERTURA\n\n- En cada apertura del dia, saluda natural, presentate como asistente de Rada Concept y da la bienvenida.\n- Usa el nombre del cliente si esta disponible.\n- Si el cliente ya pidio algo, usa solo una apertura breve antes de continuar con esa intencion.\n- Despues del saludo, sigue de forma natural con lo que el cliente pidio.\n- No uses saludos largos.",
  "templates": {
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}\n*Espacios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "messageSequences": {},
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "name": "Descubrimiento",
        "goal": "Entender el tipo de servicio de interes.",
        "advanceWhenFacts": [
          "project_context"
        ],
        "conversationGuidance": "Si el mensaje del cliente es solo un saludo, pregunta en que tipo de servicio esta interesado. En ese turno no listes servicios completos de entrada. Si el cliente ya menciona una necesidad, proyecto o servicio, registra project_context cuando aplique y continua a seleccion de servicio.",
        "allowedActions": [
          "registrar_dato"
        ],
        "collect": [
          "project_context"
        ],
        "ask": ""
      },
      {
        "id": "service_selection",
        "name": "Seleccion de servicio",
        "goal": "Explicar amablemente los servicios de Rada Concept y registrar el servicio de interes.",
        "advanceWhenFacts": [
          "service"
        ],
        "conversationGuidance": "Cuando el cliente responda el tipo de servicio, pida opciones o describa su proyecto, llama get_service_catalog. Explica maximo 1 a 3 servicios relevantes con alcance y beneficios, sin mencionar precios. Si pide precio, indica que la cotizacion se define despues de entender medidas, alcance y materiales en asesoria. Cuando el cliente elija un servicio exacto o uno claramente equivalente, registra service con el nombre canonico del catalogo. Si la necesidad puede corresponder a varias opciones, ayuda a escoger con una explicacion breve.",
        "allowedActions": [
          "consultar_catalogo",
          "registrar_dato"
        ],
        "collect": [
          "service"
        ],
        "ask": ""
      },
      {
        "id": "scheduling",
        "name": "Agenda",
        "goal": "Revisar disponibilidad y acordar fecha y hora para la cita de asesoria.",
        "afterTool": [
          {
            "tool": "check_availability",
            "when": {
              "path": "data.availability_checked",
              "equals": "true"
            },
            "setFact": {
              "key": "desired_date",
              "value": "{{data.date}}"
            }
          },
          {
            "tool": "check_availability",
            "when": {
              "path": "data.availability_checked",
              "equals": "true"
            },
            "setFact": {
              "key": "desired_time",
              "value": "{{data.time}}"
            }
          },
          {
            "tool": "check_availability",
            "when": {
              "path": "data.availability_checked",
              "equals": "true"
            },
            "setFact": {
              "key": "availability_checked",
              "value": "true"
            }
          }
        ],
        "advanceWhenFacts": [
          "availability_checked"
        ],
        "reentryOnFactChanged": [
          "service",
          "desired_date",
          "desired_time"
        ],
        "conversationGuidance": "Primero llama get_service_fulfillment con el servicio elegido. Para agenda, pide fecha si falta desired_date. Cuando tengas fecha, llama check_availability para mostrar horarios disponibles. Cuando el cliente elija hora, registra desired_time y llama check_availability con fecha y hora. Si el horario esta disponible, deja avanzar el flujo.",
        "allowedActions": [
          "resolver_tipo_atencion",
          "validar_disponibilidad",
          "registrar_dato"
        ],
        "collect": [
          "availability_checked"
        ],
        "ask": ""
      },
      {
        "id": "customer_data",
        "name": "Datos del cliente",
        "goal": "Recoger datos minimos para crear la cita.",
        "advanceWhenFacts": [
          "customer_name",
          "customer_phone"
        ],
        "conversationGuidance": "Pide en un solo mensaje los datos faltantes para la cita, en lista corta: nombre y celular de contacto. Si ya tienes uno de los datos, pide solo el que falta. Registra los datos con set_fact.",
        "allowedActions": [
          "registrar_dato"
        ],
        "collect": [
          "customer_name",
          "customer_phone"
        ],
        "ask": ""
      },
      {
        "id": "confirmation",
        "name": "Confirmacion",
        "goal": "Confirmar la cita de asesoria.",
        "advanceWhenFacts": [],
        "conversationGuidance": "Muestra un resumen breve con servicio, fecha, hora y nombre. Pide confirmacion. Cuando el cliente confirme claramente, llama create_reservation con customer_confirmed=true. Despues de crear la cita, confirma con tono cordial que quedo agendada. Si falta o cambia fecha u hora, vuelve a check_availability antes de crear la cita.",
        "allowedActions": [
          "crear_reserva",
          "validar_disponibilidad",
          "registrar_dato"
        ],
        "collect": [],
        "ask": ""
      }
    ],
    "language": {
      "actions": {
        "registrar_dato": {
          "name": "Registrar dato",
          "purpose": "Guardar datos expresados por el cliente cuando son necesarios para avanzar.",
          "tool": "set_fact"
        },
        "consultar_catalogo": {
          "name": "Consultar catalogo oficial",
          "purpose": "Presentar categorias o servicios oficiales segun la intencion del cliente.",
          "tool": "get_service_catalog"
        },
        "resolver_tipo_atencion": {
          "name": "Resolver tipo de atencion",
          "purpose": "Determinar si el servicio requiere agenda, inscripcion u otra ruta de cumplimiento.",
          "tool": "get_service_fulfillment"
        },
        "validar_disponibilidad": {
          "name": "Validar disponibilidad",
          "purpose": "Consultar agenda oficial y confirmar fecha y hora disponibles.",
          "tool": "check_availability"
        },
        "crear_reserva": {
          "name": "Crear reserva",
          "purpose": "Crear la reserva cuando los datos requeridos y verificaciones esten completos.",
          "tool": "create_reservation"
        },
        "escalar_humano": {
          "name": "Escalar a humano",
          "purpose": "Pasar la conversacion a una persona con el contexto necesario.",
          "tool": "escalate_to_human"
        },
        "reiniciar_solicitud": {
          "name": "Reiniciar solicitud",
          "purpose": "Limpiar el contexto de la solicitud actual segun la intencion del cliente.",
          "tool": "reset_flow_context"
        }
      },
      "enabled": true
    }
  },
  "globalActions": [
    {
      "id": "human_handoff",
      "priority": 100,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja, solicitud fuera del alcance o necesite cotizacion detallada inmediata.",
      "conversationGuidance": "Responde con una frase breve y cordial, y llama escalate_to_human.",
      "allowedActions": [
        "escalar_humano"
      ]
    },
    {
      "id": "restart_request",
      "priority": 70,
      "goal": "Reiniciar la solicitud actual cuando el cliente quiera cambiar completamente de servicio o empezar de nuevo.",
      "conversationGuidance": "Usa reset_flow_context cuando el cliente indique claramente que quiere cambiar la solicitud completa. Conserva datos persistentes del cliente.",
      "allowedActions": [
        "reiniciar_solicitud",
        "registrar_dato"
      ]
    }
  ],
  "factSchema": [
    {
      "key": "session.engagement",
      "role": "session.engagement",
      "label": "contexto de engagement",
      "type": "string",
      "required": false,
      "source": "session",
      "scope": "ephemeral"
    },
    {
      "key": "project_context",
      "role": "project.context",
      "label": "tipo de proyecto o necesidad",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "captureMode": "eager",
      "aliases": [
        "proyecto",
        "necesidad",
        "tipo de servicio",
        "vivienda",
        "local",
        "cocina",
        "closet",
        "remodelacion"
      ]
    },
    {
      "key": "service",
      "role": "booking.service",
      "label": "servicio de interes",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "captureMode": "eager",
      "aliases": [
        "servicio",
        "diseno",
        "interior",
        "arquitectonico",
        "mobiliario",
        "remodelacion",
        "asesoria",
        "espacio comercial"
      ]
    },
    {
      "key": "desired_date",
      "role": "booking.date",
      "label": "fecha deseada",
      "type": "date",
      "required": true,
      "source": "user",
      "scope": "request",
      "captureMode": "eager",
      "aliases": [
        "fecha",
        "dia",
        "cuando",
        "manana",
        "hoy"
      ]
    },
    {
      "key": "desired_time",
      "role": "booking.time",
      "label": "hora deseada",
      "type": "time",
      "required": true,
      "source": "user",
      "scope": "request",
      "captureMode": "eager",
      "dependsOn": [
        "service",
        "desired_date"
      ],
      "aliases": [
        "hora",
        "horario"
      ]
    },
    {
      "key": "availability_checked",
      "role": "booking.availability_checked",
      "label": "disponibilidad validada",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "ephemeral",
      "retentionDays": 1,
      "dependsOn": [
        "service",
        "desired_date",
        "desired_time"
      ]
    },
    {
      "key": "customer_name",
      "role": "customer.name",
      "label": "nombre del cliente",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer",
      "captureMode": "eager",
      "aliases": [
        "nombre",
        "cliente",
        "a nombre de"
      ]
    },
    {
      "key": "customer_phone",
      "role": "customer.phone",
      "label": "celular de contacto",
      "type": "phone",
      "required": false,
      "source": "user",
      "scope": "customer",
      "captureMode": "eager",
      "aliases": [
        "celular",
        "telefono",
        "whatsapp",
        "numero"
      ]
    },
    {
      "key": "payment_method",
      "role": "payment.method",
      "label": "metodo de pago",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "request",
      "expireOnBusinessDayChange": true
    }
  ],
  "guards": {},
  "enabledTools": [
    "set_fact",
    "get_service_catalog",
    "get_service_fulfillment",
    "check_availability",
    "create_reservation",
    "reset_flow_context",
    "escalate_to_human"
  ],
  "escalations": {
    "human": {
      "contacts": [
        "+573007047440"
      ]
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "notifications": {
    "reservation_created": {
      "enabled": false,
      "recipients": [],
      "sendMessageSequence": null
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {}
  }
}';

IF ISJSON(@SettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedRadaConcept: SettingsJson invalido.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,
         SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Asistente Rada Concept',
         N'Asistente comercial para explicar servicios de Rada Concept y agendar citas de asesoria.',
         1, @SettingsJson, @SystemPrompt, N'gpt-4.1-mini', 0.68, 8, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET BusinessId = @BusinessId,
        AgentTypeId = @AgentTypeId,
        Name = N'Asistente Rada Concept',
        Description = N'Asistente comercial para explicar servicios de Rada Concept y agendar citas de asesoria.',
        IsActive = 1,
        SettingsJson = @SettingsJson,
        SystemPromptMarkdown = @SystemPrompt,
        Model = N'gpt-4.1-mini',
        Temperature = 0.68,
        MaxToolIterations = 8,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END

DECLARE @MimosSubscriptionId UNIQUEIDENTIFIER;

SELECT TOP (1) @MimosSubscriptionId = BusinessSubscriptionId
FROM dbo.BusinessSubscriptions
WHERE BusinessId = @MimosBusinessId
  AND Status IN (1, 2, 3)
ORDER BY CreatedAt DESC;

IF @MimosSubscriptionId IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.BusinessSubscriptions WHERE BusinessId = @BusinessId AND Status IN (1, 2, 3))
    BEGIN
        UPDATE target
        SET SubscriptionPlanId     = source.SubscriptionPlanId,
            Status                 = source.Status,
            CurrentPeriodStart     = source.CurrentPeriodStart,
            CurrentPeriodEnd       = source.CurrentPeriodEnd,
            PlanCodeSnapshot       = source.PlanCodeSnapshot,
            PlanNameSnapshot       = source.PlanNameSnapshot,
            MonthlyPriceCop        = source.MonthlyPriceCop,
            IncludedCredits        = source.IncludedCredits,
            MaxVariableCostCop     = source.MaxVariableCostCop,
            MaxVariableCostPercent = source.MaxVariableCostPercent,
            ExtraCredits           = source.ExtraCredits,
            ExtraVariableCostCop   = source.ExtraVariableCostCop,
            AutoRenew              = source.AutoRenew,
            UpdatedAt              = SYSUTCDATETIME()
        FROM dbo.BusinessSubscriptions target
        CROSS JOIN dbo.BusinessSubscriptions source
        WHERE source.BusinessSubscriptionId = @MimosSubscriptionId
          AND target.BusinessId = @BusinessId
          AND target.Status IN (1, 2, 3);
    END
    ELSE
    BEGIN
        INSERT INTO dbo.BusinessSubscriptions (
            BusinessId, SubscriptionPlanId, Status, CurrentPeriodStart, CurrentPeriodEnd,
            PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits,
            MaxVariableCostCop, MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop,
            AutoRenew
        )
        SELECT
            @BusinessId, SubscriptionPlanId, Status, CurrentPeriodStart, CurrentPeriodEnd,
            PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits,
            MaxVariableCostCop, MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop,
            AutoRenew
        FROM dbo.BusinessSubscriptions
        WHERE BusinessSubscriptionId = @MimosSubscriptionId;
    END
END

PRINT N'SeedRadaConcept: negocio, servicios y agente configurados.';
GO
