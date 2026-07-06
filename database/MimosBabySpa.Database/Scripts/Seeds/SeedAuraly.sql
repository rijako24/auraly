-- =============================================================================
-- SeedAuraly.sql
--
-- Crea/actualiza el negocio AURALY, el empleado Geraldine Beltran y el agente
-- Aly para explicar empleados digitales y agendar demos. Idempotente.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @TenantId        UNIQUEIDENTIFIER = 'A0A10000-0000-0000-0000-000000000000';
DECLARE @BusinessId      UNIQUEIDENTIFIER = 'A0A10000-0000-0000-0000-000000000001';
DECLARE @AgentId         UNIQUEIDENTIFIER = 'A0A10000-0000-0000-0000-000000000002';
DECLARE @EmployeeId      UNIQUEIDENTIFIER = 'A0A10000-0000-0000-0000-000000000003';
DECLARE @CategoryId      UNIQUEIDENTIFIER = 'A0A10000-0000-0000-0000-000000000010';
DECLARE @AgentTypeId     UNIQUEIDENTIFIER;

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.Tenants (TenantId, Name, Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'AURALY', N'admin@auraly.ai', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Tenants
    SET Name = N'AURALY',
        Email = N'admin@auraly.ai',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE TenantId = @TenantId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    INSERT INTO dbo.Businesses
        (BusinessId, TenantId, Name, Description, Address, Phone, Email, Website, IsActive, CreatedAt)
    VALUES
        (@BusinessId, @TenantId, N'AURALY',
         N'Plataforma de empleados digitales configurables para WhatsApp, ventas, agenda, soporte, pagos y seguimiento comercial 24/7.',
         N'Remoto', N'+573000000000', N'admin@auraly.ai', N'https://auraly.ai', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Businesses
    SET TenantId = @TenantId,
        Name = N'AURALY',
        Description = N'Plataforma de empleados digitales configurables para WhatsApp, ventas, agenda, soporte, pagos y seguimiento comercial 24/7.',
        Address = N'Remoto',
        Phone = N'+573000000000',
        Email = N'admin@auraly.ai',
        Website = N'https://auraly.ai',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories WHERE ServiceCategoryId = @CategoryId)
BEGIN
    INSERT INTO dbo.ServiceCategories
        (ServiceCategoryId, BusinessId, Name, Description, DisplayOrder, IsActive, CreatedAt)
    VALUES
        (@CategoryId, @BusinessId, N'Empleados digitales',
         N'Diagnostico, implementacion y automatizacion comercial con empleados digitales para WhatsApp.',
         1, 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.ServiceCategories
    SET BusinessId = @BusinessId,
        Name = N'Empleados digitales',
        Description = N'Diagnostico, implementacion y automatizacion comercial con empleados digitales para WhatsApp.',
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
    DurationMinutes INT NOT NULL,
    DisplayOrder INT NOT NULL
);

INSERT INTO @Services (ServiceId, ServiceName, Description, DurationMinutes, DisplayOrder)
VALUES
('A0A10000-0000-0000-0000-000000000101', N'Demo AURALY',
 N'Sesion de diagnostico para mapear el flujo actual de WhatsApp, detectar cuellos de botella y mostrar como Aly o un empleado digital puede responder 24/7, calificar leads, explicar servicios, agendar, cobrar y hacer seguimiento.',
 60, 1),
('A0A10000-0000-0000-0000-000000000102', N'Empleado digital de ventas 24/7',
 N'Asesor digital configurable para responder preguntas, entender intencion de compra, recomendar servicios, manejar objeciones, capturar datos, maximizar conversion y escalar a humano cuando corresponde.',
 60, 2),
('A0A10000-0000-0000-0000-000000000103', N'Automatizacion de agenda, pagos y seguimiento',
 N'Flujos conectados a disponibilidad, reservas, pagos, recordatorios, recuperacion de conversaciones, plantillas de WhatsApp y trazabilidad operativa para que ninguna oportunidad quede sin accion.',
 60, 3);

MERGE dbo.Services AS target
USING @Services AS source
   ON target.BusinessId = @BusinessId
  AND target.ServiceName = source.ServiceName
WHEN MATCHED THEN
    UPDATE SET
        Description = source.Description,
        DurationMinutes = source.DurationMinutes,
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
    VALUES (source.ServiceId, @BusinessId, source.ServiceName, source.Description, source.DurationMinutes, 0,
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
    VALUES (@EmployeeId, @BusinessId, N'Geraldine Beltran', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Employees
    SET BusinessId = @BusinessId,
        Name = N'Geraldine Beltran',
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

IF EXISTS (SELECT 1 FROM dbo.BusinessSchedulingSettings WHERE BusinessId = @BusinessId)
BEGIN
    UPDATE dbo.BusinessSchedulingSettings
    SET SlotIntervalMinutes = 60,
        BufferBetweenAppointmentsMinutes = 0,
        MinimumLeadTimeMinutes = 0,
        RequireEmployee = 1,
        EmployeeStrategy = N'least_versatile',
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END
ELSE
BEGIN
    INSERT INTO dbo.BusinessSchedulingSettings
        (BusinessSchedulingSettingsId, BusinessId, SlotIntervalMinutes, BufferBetweenAppointmentsMinutes, MinimumLeadTimeMinutes, RequireEmployee, EmployeeStrategy, CreatedAt)
    VALUES
        (NEWID(), @BusinessId, 60, 0, 0, 1, N'least_versatile', GETUTCDATE());
END

DECLARE @Hours TABLE (DayOfWeek INT NOT NULL, OpenTime TIME(0) NOT NULL, CloseTime TIME(0) NOT NULL);
INSERT INTO @Hours VALUES
(1, CONVERT(TIME(0), '09:00'), CONVERT(TIME(0), '12:00')),
(1, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),
(2, CONVERT(TIME(0), '09:00'), CONVERT(TIME(0), '12:00')),
(2, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),
(3, CONVERT(TIME(0), '09:00'), CONVERT(TIME(0), '12:00')),
(3, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),
(4, CONVERT(TIME(0), '09:00'), CONVERT(TIME(0), '12:00')),
(4, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),
(5, CONVERT(TIME(0), '09:00'), CONVERT(TIME(0), '12:00')),
(5, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '17:00'));

MERGE dbo.BusinessWorkingHours AS target
USING @Hours AS source
   ON target.BusinessId = @BusinessId
  AND target.DayOfWeek = source.DayOfWeek
  AND target.OpenTime = source.OpenTime
WHEN MATCHED THEN
    UPDATE SET CloseTime = source.CloseTime,
               IsActive = 1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (BusinessWorkingHourId, BusinessId, DayOfWeek, OpenTime, CloseTime, IsActive, CreatedAt)
    VALUES (NEWID(), @BusinessId, source.DayOfWeek, source.OpenTime, source.CloseTime, 1, GETUTCDATE());

UPDATE dbo.BusinessWorkingHours
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND NOT EXISTS (
      SELECT 1
      FROM @Hours h
      WHERE h.DayOfWeek = BusinessWorkingHours.DayOfWeek
        AND h.OpenTime = BusinessWorkingHours.OpenTime
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
  "temperature": 0.62,
  "maxToolIterations": 8,
  "historyWindowSize": 24,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "Eres Aly, empleada digital de AURALY por WhatsApp. Tu mision es explicar con claridad que hace AURALY, que problemas resuelve y guiar a la persona hasta agendar una demo. Hablas en espanol con tono consultivo, humano, directo y comercial, sin sonar robotica. Responde breve, ordenado y con preguntas utiles para avanzar.",
  "policies": "## PROPUESTA DE VALOR\n\n- AURALY crea empleados digitales configurables que trabajan 24/7 en WhatsApp y canales conversacionales.\n- Resolvemos chats sin responder, tiempos de espera altos, leads sin seguimiento, equipos saturados, agendas manuales, pagos abandonados y falta de trazabilidad comercial.\n- Los empleados digitales pueden explicar servicios, calificar leads, recomendar opciones, resolver preguntas frecuentes, agendar demos o citas, generar pagos, recuperar conversaciones y escalar a humanos con historial completo.\n- Enfatiza beneficios: disponibilidad 24/7, velocidad de respuesta, conversion, consistencia de marca, automatizacion de tareas repetitivas, datos estructurados, historial, medicion de consumo y configuracion por negocio.\n- AURALY no reemplaza al equipo humano: libera tiempo operativo y deja al humano los casos sensibles, estrategicos o de alto valor.\n- Evita prometer resultados exactos. Habla de maximizar ventas y mejorar conversion como objetivo, no como garantia.\n- La meta del flujo es agendar una demo AURALY.\n\n## APERTURA\n\n- En cada apertura del dia, saluda natural, presentate como Aly de AURALY y da la bienvenida.\n- Usa el nombre del cliente si esta disponible.\n- Si el cliente ya pidio algo, usa solo una apertura breve antes de continuar con esa intencion.\n- Despues del saludo, si todavia no sabes el tipo de negocio, pregunta primero que tipo de negocio quiere automatizar.\n- No uses saludos largos.",  "messageSequences": {
    "web_demo_follow_up": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "auraly_demo_engagement",
          "language": "es_CO",
          "bodyParameters": ["{CustomerName}", "{CompanyName}"]
        }
      ]
    }
  },
  "templates": {
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}*Espacios disponibles para {{date_formatted}}*\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "name": "Diagnostico inicial",
        "goal": "Identificar que tipo de negocio quiere automatizar la persona.",
        "hint": "Si falta business_type, pregunta primero que tipo de negocio quiere automatizar. Si ya lo dice, registra business_type con set_fact y avanza. No listes beneficios al primer turno; primero entiende el negocio.",
        "allowedTools": ["set_fact"],
        "advanceWhenFacts": ["business_type"]
      },
      {
        "id": "business_context",
        "name": "Contexto del negocio",
        "goal": "Entender el problema, canal principal y volumen aproximado antes de recomendar.",
        "hint": "Pregunta solo lo que falte en una lista corta: principal cuello de botella, canal donde llegan los clientes y volumen aproximado de conversaciones. Registra pain_point, main_channel y conversation_volume cuando los entreguen. Si el usuario no sabe el volumen, acepta una estimacion cualitativa.",
        "allowedTools": ["set_fact"],
        "advanceWhenFacts": ["pain_point", "main_channel"]
      },
      {
        "id": "value_explanation",
        "name": "Explicacion de valor",
        "goal": "Explicar que hacemos, servicios y ventajas conectadas al problema del cliente.",
        "hint": "Llama get_service_catalog para usar servicios oficiales. Explica maximo 4 capacidades relevantes para el pain_point: atencion 24/7, calificacion de leads, agenda, pagos, seguimiento, recuperacion, analytics, handoff humano e integraciones. Conecta cada beneficio con el problema mencionado. Cierra recomendando la demo en vivo de AURALY y pregunta si quiere ver horarios. Despues de explicar el valor, registra value_explained=true con set_fact. No preguntes ni muestres seleccion de servicio.",
        "allowedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["value_explained"]
      },
      {
        "id": "scheduling",
        "name": "Agenda de demo",
        "goal": "Mostrar disponibilidad y validar fecha/hora para la demo.",
        "hint": "La demo en vivo de AURALY es el servicio tecnico por defecto. No preguntes servicio ni muestres seleccion de servicio. Primero llama get_service_fulfillment con Demo AURALY. Si falta desired_date, pide fecha. Cuando tengas fecha, llama check_availability con service=Demo AURALY y muestra horarios disponibles. Cuando el cliente elija hora, registra desired_time y llama check_availability con service=Demo AURALY, fecha y hora. Si esta disponible, deja avanzar.",
        "allowedTools": ["get_service_fulfillment", "check_availability", "set_fact"],
        "afterTool": [
          {
            "tool": "check_availability",
            "when": { "path": "data.availability_checked", "equals": "true" },
            "setFact": { "key": "desired_date", "value": "{{data.date}}" }
          },
          {
            "tool": "check_availability",
            "when": { "path": "data.availability_checked", "equals": "true" },
            "setFact": { "key": "desired_time", "value": "{{data.time}}" }
          },
          {
            "tool": "check_availability",
            "when": { "path": "data.availability_checked", "equals": "true" },
            "setFact": { "key": "availability_checked", "value": "true" }
          }
        ],
        "advanceWhenFacts": ["availability_checked"],
        "reentryOnFactChanged": ["desired_date", "desired_time"]
      },
      {
        "id": "customer_data",
        "name": "Datos para la demo",
        "goal": "Recoger datos minimos para confirmar la demo.",
        "hint": "Pide en un solo mensaje los datos faltantes: nombre, empresa y correo. El telefono viene del canal. Registra customer_name, company_name y customer_email si los entregan. Si el cliente no quiere dar correo, continua con nombre y empresa.",
        "allowedTools": ["set_fact"],
        "advanceWhenFacts": ["customer_name", "company_name"]
      },
      {
        "id": "confirmation",
        "name": "Confirmacion de demo",
        "goal": "Confirmar la demo AURALY.",
        "hint": "Muestra resumen breve: demo, fecha, hora, nombre, empresa y telefono. Pide confirmacion. Cuando confirme claramente, llama create_reservation con customer_confirmed=true. Despues confirma que Geraldine Beltran o el equipo AURALY tendra el contexto para la demo.",
        "allowedTools": ["create_reservation", "check_availability", "set_fact"],
        "advanceWhenFacts": []
      }
    ]
  },
  "globalActions": [
    {
      "id": "human_handoff",
      "priority": 100,
      "goal": "Escalar a humano cuando lo pidan o cuando la solicitud sea alianza, soporte sensible, compra enterprise o caso fuera de alcance.",
      "hint": "Responde con una frase breve y cordial, resume el contexto y llama escalate_to_human.",
      "allowedTools": ["escalate_to_human"]
    },
    {
      "id": "restart_demo_flow",
      "priority": 70,
      "goal": "Reiniciar la solicitud si el cliente cambia de tema o quiere empezar de nuevo.",
      "hint": "Usa reset_flow_context solo si el cliente lo pide claramente o cambia por completo el objetivo.",
      "allowedTools": ["reset_flow_context", "set_fact"]
    }
  ],
  "factSchema": [
    { "key": "session.engagement", "role": "session.engagement", "label": "contexto de engagement", "type": "string", "required": false, "source": "session", "scope": "ephemeral" },
    { "key": "pain_point", "role": "sales.pain_point", "label": "cuello de botella", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "aliases": ["problema", "dolor", "cuello de botella", "necesidad", "reto"] },
    { "key": "business_type", "role": "business.type", "label": "tipo de negocio", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "aliases": ["negocio", "empresa", "industria", "sector"] },
    { "key": "main_channel", "role": "business.channel", "label": "canal principal", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "aliases": ["whatsapp", "instagram", "web", "canal"] },
    { "key": "conversation_volume", "role": "business.volume", "label": "volumen de conversaciones", "type": "string", "required": false, "source": "user", "scope": "request", "captureMode": "eager", "aliases": ["volumen", "chats", "mensajes", "leads"] },
    { "key": "value_explained", "role": "sales.value_explained", "label": "valor explicado", "type": "string", "required": false, "source": "system", "scope": "ephemeral", "retentionDays": 1 },
    { "key": "service", "role": "booking.service", "label": "servicio tecnico", "type": "string", "required": false, "source": "system", "scope": "request" },
    { "key": "desired_date", "role": "booking.date", "label": "fecha deseada", "type": "date", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "aliases": ["fecha", "dia", "cuando", "manana", "hoy"] },
    { "key": "desired_time", "role": "booking.time", "label": "hora deseada", "type": "time", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "dependsOn": ["service", "desired_date"], "aliases": ["hora", "horario"] },
    { "key": "availability_checked", "role": "booking.availability_checked", "label": "disponibilidad validada", "type": "string", "required": false, "source": "system", "scope": "ephemeral", "retentionDays": 1, "dependsOn": ["service", "desired_date", "desired_time"] },
    { "key": "customer_name", "role": "customer.name", "label": "nombre", "type": "string", "required": true, "source": "user", "scope": "customer", "captureMode": "eager", "aliases": ["nombre", "mi nombre", "contacto"] },
    { "key": "company_name", "role": "customer.company", "label": "empresa", "type": "string", "required": true, "source": "user", "scope": "customer", "captureMode": "eager", "aliases": ["empresa", "compania", "negocio"] },
    { "key": "customer_phone", "role": "customer.phone", "label": "telefono", "type": "phone", "required": true, "source": "channel", "scope": "customer", "aliases": ["telefono", "celular", "whatsapp"] },
    { "key": "customer_email", "role": "customer.email", "label": "correo", "type": "email", "required": false, "source": "user", "scope": "customer", "captureMode": "eager", "aliases": ["correo", "email"] },
    { "key": "payment_method", "role": "payment.method", "label": "metodo de pago", "type": "string", "required": false, "source": "system", "scope": "request", "expireOnBusinessDayChange": true }
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
    "human": { "contacts": ["+573000000000"] },
    "external": { "enabled": false, "events": {} }
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
    THROW 51000, 'SeedAuraly: SettingsJson invalido.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,
         SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Aly',
         N'Empleada digital de AURALY para explicar servicios, calificar leads y agendar demos.',
         1, @SettingsJson, @SystemPrompt, N'gpt-4.1-mini', 0.62, 8, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET BusinessId = @BusinessId,
        AgentTypeId = @AgentTypeId,
        Name = N'Aly',
        Description = N'Empleada digital de AURALY para explicar servicios, calificar leads y agendar demos.',
        IsActive = 1,
        SettingsJson = @SettingsJson,
        SystemPromptMarkdown = @SystemPrompt,
        Model = N'gpt-4.1-mini',
        Temperature = 0.62,
        MaxToolIterations = 8,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END

DECLARE @AuralyPhoneNumber NVARCHAR(20) = N'+573117324418';
DECLARE @AuralyWhatsAppPhoneId NVARCHAR(100) = N'1207729672420835';
DECLARE @AuralyWhatsAppBusinessAccountId NVARCHAR(100);
DECLARE @AuralyWhatsAppAccessToken NVARCHAR(500);
DECLARE @AuralyWhatsAppNumberId UNIQUEIDENTIFIER;

SELECT TOP (1)
    @AuralyWhatsAppNumberId = BusinessWhatsAppNumberId,
    @AuralyWhatsAppAccessToken = WhatsAppAccessToken,
    @AuralyWhatsAppBusinessAccountId = WhatsAppBusinessAccountId
FROM dbo.BusinessWhatsAppNumbers
WHERE (WhatsAppPhoneNumberId = @AuralyWhatsAppPhoneId OR BusinessId = @BusinessId)
  AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
ORDER BY
    CASE WHEN WhatsAppPhoneNumberId = @AuralyWhatsAppPhoneId THEN 0 ELSE 1 END,
    IsActive DESC,
    CreatedAt DESC;

IF @AuralyWhatsAppAccessToken IS NULL
BEGIN
    PRINT N'SeedAuraly: no se encontro token propio de Auraly; omitiendo numero WhatsApp.';
END
ELSE IF @AuralyWhatsAppNumberId IS NULL
BEGIN
    INSERT INTO dbo.BusinessWhatsAppNumbers (
        BusinessWhatsAppNumberId,
        BusinessId,
        AgentId,
        PhoneNumber,
        WhatsAppBusinessAccountId,
        WhatsAppPhoneNumberId,
        WhatsAppAccessToken,
        IsActive,
        CreatedAt
    )
    VALUES (
        NEWID(),
        @BusinessId,
        @AgentId,
        @AuralyPhoneNumber,
        @AuralyWhatsAppBusinessAccountId,
        @AuralyWhatsAppPhoneId,
        @AuralyWhatsAppAccessToken,
        1,
        GETUTCDATE()
    );
END
ELSE
BEGIN
    UPDATE dbo.BusinessWhatsAppNumbers
    SET BusinessId = @BusinessId,
        AgentId = @AgentId,
        PhoneNumber = @AuralyPhoneNumber,
        WhatsAppPhoneNumberId = @AuralyWhatsAppPhoneId,
        WhatsAppBusinessAccountId = @AuralyWhatsAppBusinessAccountId,
        WhatsAppAccessToken = @AuralyWhatsAppAccessToken,
        IsActive = 1
    WHERE BusinessWhatsAppNumberId = @AuralyWhatsAppNumberId;
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

PRINT N'SeedAuraly: negocio AURALY, empleado Geraldine Beltran y agente Aly configurados.';
GO


