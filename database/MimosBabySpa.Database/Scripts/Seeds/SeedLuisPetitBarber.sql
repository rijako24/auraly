-- =============================================================================
-- SeedLuisPetitBarber.sql
--
-- Crea/actualiza el negocio Luis Petit Profesional Barber, su catalogo de
-- servicios y el agente Luis para reservas con anticipo del 50%.
-- Idempotente.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @TenantId        UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000000';
DECLARE @BusinessId      UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000001';
DECLARE @AgentId         UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000002';
DECLARE @EmployeeId      UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000003';
DECLARE @CategoryId      UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000010';
DECLARE @AgentTypeId     UNIQUEIDENTIFIER;

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.Tenants (TenantId, Name, Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'Luis Petit Profesional Barber', N'admin@luispetitbarber.com', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Tenants
    SET Name = N'Luis Petit Profesional Barber',
        Email = N'admin@luispetitbarber.com',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE TenantId = @TenantId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    INSERT INTO dbo.Businesses
        (BusinessId, TenantId, Name, Description, Address, Phone, Email, Website, IsActive, CreatedAt)
    VALUES
        (@BusinessId, @TenantId, N'Luis Petit Profesional Barber',
         N'Barberia profesional enfocada en elegancia, detalle, puntualidad y atencion personalizada para cada cliente.',
         N'Por configurar', N'+573042052007', N'admin@luispetitbarber.com', N'', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Businesses
    SET TenantId = @TenantId,
        Name = N'Luis Petit Profesional Barber',
        Description = N'Barberia profesional enfocada en elegancia, detalle, puntualidad y atencion personalizada para cada cliente.',
        Address = N'Por configurar',
        Phone = N'+573042052007',
        Email = N'admin@luispetitbarber.com',
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
        (@CategoryId, @BusinessId, N'Servicios de barberia',
         N'Cortes, barba, cejas, lavado profundo, domicilio y tratamientos especiales.',
         1, 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.ServiceCategories
    SET BusinessId = @BusinessId,
        Name = N'Servicios de barberia',
        Description = N'Cortes, barba, cejas, lavado profundo, domicilio y tratamientos especiales.',
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
    Price DECIMAL(18, 2) NOT NULL,
    DisplayOrder INT NOT NULL
);

INSERT INTO @Services (ServiceId, ServiceName, Description, DurationMinutes, Price, DisplayOrder)
VALUES
('BABA0000-0000-0000-0000-000000000101', N'Corte de niño',
 N'Corte infantil con trato paciente, detalle y acabado limpio. Servicio personalizado para una experiencia comoda y puntual.',
 60, 25000.00, 1),
('BABA0000-0000-0000-0000-000000000102', N'Corte de adulto',
 N'Corte profesional para adulto, adaptado al estilo del cliente, con atencion al detalle y acabado elegante.',
 60, 30000.00, 2),
('BABA0000-0000-0000-0000-000000000103', N'Corte + barba',
 N'Corte de cabello y arreglo de barba con perfilado, simetria y acabado profesional.',
 60, 40000.00, 3),
('BABA0000-0000-0000-0000-000000000104', N'Diseño de cejas',
 N'Diseno y perfilado de cejas para armonizar el rostro con un acabado natural y pulido.',
 60, 10000.00, 4),
('BABA0000-0000-0000-0000-000000000105', N'Lavado profundo',
 N'Limpieza profunda del cabello y cuero cabelludo para una sensacion fresca, cuidada y renovada.',
 60, 15000.00, 5),
('BABA0000-0000-0000-0000-000000000106', N'Servicio a domicilio',
 N'Servicio de barberia a domicilio desde $100.000 COP. El valor final y disponibilidad dependen de ubicacion, horario y condiciones del servicio.',
 60, 100000.00, 6),
('BABA0000-0000-0000-0000-000000000107', N'Coloración / Keratina / Tratamientos especiales',
 N'Coloracion, keratina y tratamientos especiales a partir de $120.000 COP. El valor puede variar segun diagnostico, longitud, tecnica y producto requerido.',
 60, 120000.00, 7);

MERGE dbo.Services AS target
USING @Services AS source
   ON target.BusinessId = @BusinessId
  AND target.ServiceName = source.ServiceName
WHEN MATCHED THEN
    UPDATE SET
        Description = source.Description,
        DurationMinutes = source.DurationMinutes,
        Price = source.Price,
        IncludeInCheckoutTotal = 1,
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
    VALUES (source.ServiceId, @BusinessId, source.ServiceName, source.Description, source.DurationMinutes, source.Price,
            1, @CategoryId, 0, 0, 0, NULL, 1, GETUTCDATE());

UPDATE s
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
FROM dbo.Services s
WHERE s.BusinessId = @BusinessId
  AND NOT EXISTS (SELECT 1 FROM @Services src WHERE src.ServiceName = s.ServiceName);

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeId = @EmployeeId)
BEGIN
    INSERT INTO dbo.Employees (EmployeeId, BusinessId, Name, IsActive, CreatedAt)
    VALUES (@EmployeeId, @BusinessId, N'Luis Petit', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Employees
    SET BusinessId = @BusinessId,
        Name = N'Luis Petit',
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
        RequireEmployee = 1,
        EmployeeStrategy = N'least_versatile',
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END
ELSE
BEGIN
    INSERT INTO dbo.BusinessSchedulingSettings
        (BusinessSchedulingSettingsId, BusinessId, SlotIntervalMinutes, BufferBetweenAppointmentsMinutes, RequireEmployee, EmployeeStrategy, CreatedAt)
    VALUES
        (NEWID(), @BusinessId, 60, 0, 1, N'least_versatile', GETUTCDATE());
END

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
  "temperature": 0.66,
  "maxToolIterations": 8,
  "historyWindowSize": 24,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "Eres Luis, asistente de reservas de Luis Petit Profesional Barber por WhatsApp. Atiendes en espanol con tono elegante, cercano, profesional y puntual. Tu trabajo es ayudar a elegir el servicio correcto, revisar disponibilidad y guiar al cliente hasta pagar el anticipo para asegurar la cita.",
  "policies": "## MARCA Y ATENCION\n\n- En Luis Petit Profesional Barber, cada servicio es una experiencia personalizada, enfocada en la elegancia y el detalle.\n- Responde siempre en espanol, breve y claro.\n- Ofrece horarios en bloques de una hora.\n- Confirma reservas solo cuando el pago del anticipo este aprobado por el webhook o la herramienta indique reserva confirmada.\n- Para confirmar una reserva se requiere un anticipo del 50% del valor del servicio.\n- Servicio a domicilio inicia desde $100.000 COP y depende de ubicacion, horario y condiciones.\n- Coloracion, keratina y tratamientos especiales inician desde $120.000 COP y pueden variar segun diagnostico, longitud, tecnica y producto.\n- Para una cotizacion exacta de un servicio variable, pide contexto breve y escala a humano cuando haga falta.\n- En cada visita, invita a disfrutar de un ambiente exclusivo, puntualidad garantizada y atencion personalizada de principio a fin.",
  "messageSequences": {
    "reservation_confirmed": {
      "messages": [
        { "body": "Tu reserva en Luis Petit Profesional Barber ha sido confirmada para el {Date} a las {Time}." },
        { "body": "Te esperamos para una experiencia personalizada, con puntualidad garantizada y atencion al detalle de principio a fin." }
      ]
    },
    "reservation_confirmation_request": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_confirmation_request",
          "language": "es_CO",
          "bodyParameters": ["{CustomerName}", "{Service}", "{Date}", "{Time}"],
          "buttons": [
            { "id": "reservation_attendance:confirm:{job_id}", "title": "Confirmar" },
            { "id": "reservation_attendance:reschedule:{job_id}", "title": "Reprogramar" }
          ]
        }
      ]
    },
    "reservation_reminder": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_reminder",
          "language": "es_CO",
          "bodyParameters": ["{CustomerName}", "{Service}", "{Date}", "{Time}"]
        }
      ]
    },
    "payment_slot_taken": {
      "messages": [
        { "body": "Recibimos tu pago de ${amount} {currency}. Tu comprobante quedo registrado." },
        { "body": "Lo sentimos, el horario de las {Time} ya no esta disponible porque otro cliente lo reservo primero. Tu pago esta seguro. Quieres elegir otro horario? Opciones: {slots}." }
      ]
    },
    "reservation_created": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_created",
          "language": "es_CO",
          "bodyParameters": ["{CustomerName}", "{Service}", "{Date}", "{Time}", "{Total}"]
        }
      ]
    }
  },
  "webhooks": {
    "wompi": {
      "reservation_created": { "sendMessageSequence": "reservation_confirmed" },
      "slot_unavailable_after_payment": { "sendMessageSequence": "payment_slot_taken" }
    }
  },
  "notifications": {
    "reservation_created": {
      "enabled": true,
      "recipients": ["573042052007"],
      "sendMessageSequence": "reservation_created"
    }
  },
  "reservationAutomations": {
    "confirmation": {
      "enabled": true,
      "trigger": { "type": "relative", "hoursBefore": 24 },
      "sendMessageSequence": "reservation_confirmation_request"
    },
    "reminder": {
      "enabled": true,
      "trigger": { "type": "fixedLocalTime", "daysBefore": 0, "time": "08:00" },
      "sendMessageSequence": "reservation_reminder"
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
      }
    }
  },
  "templates": {
    "checkout_with_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}} {{currency}}\n{{#each addons}}\n- {{name}}: ${{price}}{{checkout_note}}\n{{/each}}\n- *TOTAL: ${{total}} {{currency}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n\nPara asegurar tu reserva, solicitamos un anticipo del {{deposit_pct}}% del valor del servicio.\n\n*Anticipo:* ${{deposit}} {{currency}}\n\nPaga de forma segura aqui:\n{{link_url}}\n\nCuando el pago sea aprobado, tu reserva quedara confirmada automaticamente.",
    "checkout_no_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}} {{currency}}\n- *TOTAL: ${{total}} {{currency}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n\nConfirmas la reserva con esta informacion?",
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}*Horarios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each slots}}\n- {{this}}\n{{/each}}\n\nCual prefieres?"
  },
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "name": "Descubrimiento",
        "goal": "Dar la bienvenida, presentarse, mostrar los servicios devueltos por el catalogo y preguntar por el servicio de interes.",
        "hint": "En la bienvenida o cuando el cliente salude o pida informacion inicial, llama get_service_catalog y responde presentandote: Hola, soy Luis de Luis Petit Profesional Barber. Luego muestra una lista corta usando solo servicios, precios, duraciones y notas devueltas por get_service_catalog. Cierra exactamente con: En que servicio estas interesado el dia de hoy? Si el cliente ya menciona un servicio claro, registra service con el nombre canonico del catalogo.",
        "allowedTools": ["get_service_catalog", "set_fact"],
        "advanceWhenFacts": ["service"]
      },
      {
        "id": "scheduling",
        "name": "Agenda",
        "goal": "Revisar disponibilidad y validar fecha y hora para una reserva por hora.",
        "hint": "Todos los servicios de Luis Petit se agendan como reserva. Si falta fecha, pregunta: Para que dia deseas el servicio? Si el cliente da dia y hora juntos, registra desired_date y desired_time y llama check_availability con fecha y hora en el mismo turno. Si solo tienes fecha, llama check_availability sin hora para mostrar horarios disponibles. Usa horarios en bloques de una hora. Cuando el cliente elija una hora de los horarios presentados, registra desired_time y llama check_availability con fecha y hora. Si el horario esta disponible, deja avanzar el flujo.",
        "allowedTools": ["check_availability", "set_fact"],
        "afterTool": [
          {
            "tool": "check_availability",
            "when": { "path": "data.verbal_status", "equals": "horario_disponible_no_reservado" },
            "setFacts": {
              "fulfillment_ready": "reservation"
            }
          }
        ],
        "advanceWhenFacts": ["fulfillment_ready"],
        "reentryOnFactChanged": ["service", "desired_date", "desired_time"]
      },
      {
        "id": "customer_data",
        "name": "Datos del cliente",
        "goal": "Recoger los datos minimos para preparar el anticipo.",
        "hint": "Confirma brevemente servicio, fecha y hora. Pide solo los datos faltantes: nombre del cliente y, si no viene del canal, telefono.",
        "allowedTools": ["set_fact"],
        "advanceWhenFacts": ["customer_name", "customer_phone"]
      },
      {
        "id": "finalization",
        "name": "Cierre con anticipo",
        "goal": "Preparar el resumen, generar el link de anticipo y esperar confirmacion automatica de pago.",
        "hint": "Si ya estan servicio, fecha, hora, nombre y telefono, llama prepare_checkout. Si el cliente dice que ya pago, usa verify_payment. Si falta o cambia fecha/hora antes del resumen, llama check_availability de nuevo. Si el cliente quiere cambiar servicio u horario despues del link, actualiza facts y vuelve a prepare_checkout para generar el resumen correcto.",
        "allowedTools": [
          "prepare_checkout",
          "verify_payment",
          "get_service_catalog",
          "check_availability",
          "set_fact",
          "reset_flow_context",
          "send_message_sequence"
        ],
        "advanceWhenFacts": []
      }
    ]
  },
  "globalActions": [
    {
      "id": "human_escalation",
      "priority": 1000,
      "goal": "Escalar a una persona cuando el cliente lo pida, este inconforme, necesite cotizacion exacta de servicio variable o la solicitud salga del alcance del bot.",
      "hint": "Responde con una frase breve y cordial, resume la necesidad y llama escalate_to_human.",
      "allowedTools": ["escalate_to_human"]
    },
    {
      "id": "complete_paid_slot_assignment",
      "priority": 950,
      "goal": "Completar la asignacion de horario cuando un pago confirmado quedo sin reserva porque el horario original ya no estaba disponible.",
      "hint": "Usa esta ruta solo cuando el cliente este eligiendo nuevo horario para un pago ya confirmado. Primero valida el horario con check_availability usando el servicio original; si esta disponible, llama assign_paid_slot con date y time. Si no esta disponible, ofrece los horarios devueltos por check_availability.",
      "allowedTools": ["check_availability", "assign_paid_slot", "set_fact"]
    },
    {
      "id": "manage_existing_reservation",
      "priority": 900,
      "goal": "Gestionar reservas existentes cuando el cliente quiera confirmar asistencia, cambiar, reagendar, cambiar servicio o cancelar una reserva ya creada.",
      "hint": "Usa esta ruta antes del flujo de reserva nueva. Si el cliente confirma que asistira, usa confirm_reservation_attendance. Primero identifica la reserva con get_customer_reservations cuando haga falta. Para cambios, usa prepare_reservation_change y aplica con confirm_reservation_change solo despues de confirmacion clara. Si hay varias reservas, pregunta cual por fecha y servicio; nunca pidas UUID al cliente. Usa suspend_reservation solo cuando la intencion de suspender o cancelar sea clara, o despues de confirmacion explicita.",
      "allowedTools": ["get_customer_reservations", "confirm_reservation_attendance", "prepare_reservation_change", "confirm_reservation_change", "suspend_reservation"]
    }
  ],
  "factSchema": [
    { "key": "session.engagement", "role": "session.engagement", "label": "contexto de engagement", "type": "string", "required": false, "source": "session", "scope": "ephemeral" },
    { "key": "booking_intent", "role": "booking.intent", "label": "intencion de reserva", "type": "string", "required": false, "source": "user", "scope": "request", "captureMode": "eager", "aliases": ["reservar", "cita", "agenda", "servicio", "precio", "corte", "barba", "cejas", "lavado", "domicilio", "tratamiento"] },
    { "key": "service", "role": "booking.service", "label": "servicio", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "retentionDays": 7, "aliases": ["servicio", "corte", "barba", "cejas", "lavado", "domicilio", "coloracion", "keratina", "tratamiento"] },
    { "key": "desired_date", "role": "booking.date", "label": "fecha deseada", "type": "date", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "retentionDays": 7, "aliases": ["fecha", "dia", "cuando", "hoy", "manana"] },
    { "key": "desired_time", "role": "booking.time", "label": "hora deseada", "type": "time", "required": true, "source": "user", "scope": "request", "captureMode": "eager", "retentionDays": 7, "aliases": ["hora", "horario"] },
    { "key": "fulfillment_ready", "role": "checkout.fulfillment_ready", "label": "agenda validada", "type": "string", "required": false, "source": "system", "scope": "ephemeral", "expireOnBusinessDayChange": true },
    { "key": "service_notes", "role": "booking.notes", "label": "notas del servicio", "type": "string", "required": false, "source": "user", "scope": "request", "captureMode": "eager", "retentionDays": 7, "aliases": ["direccion", "ubicacion", "barrio", "domicilio", "color", "keratina", "tratamiento", "nota"] },
    { "key": "customer_name", "role": "customer.name", "label": "nombre del cliente", "type": "string", "required": true, "source": "user", "scope": "customer", "captureMode": "eager", "aliases": ["nombre", "cliente", "a nombre de", "mi nombre"] },
    { "key": "customer_phone", "role": "customer.phone", "label": "telefono del cliente", "type": "phone", "required": true, "source": "channel", "scope": "customer", "aliases": ["telefono", "celular", "whatsapp", "numero"] },
    { "key": "customer_email", "role": "customer.email", "label": "email del cliente", "type": "email", "required": false, "source": "user", "scope": "customer", "captureMode": "eager", "aliases": ["email", "correo"] }
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
    "check_availability",
    "prepare_checkout",
    "create_reservation",
    "assign_paid_slot",
    "suspend_reservation",
    "get_customer_reservations",
    "confirm_reservation_attendance",
    "prepare_reservation_change",
    "confirm_reservation_change",
    "verify_payment",
    "escalate_to_human",
    "reset_flow_context",
    "send_message_sequence"
  ],
  "escalations": {
    "human": { "contacts": ["+573042052007"], "killSwitchPhrases": [
      "quiero hablar con un humano",
      "quiero hablar con una persona",
      "agente real",
      "operador",
      "hablar con alguien",
      "hablar con ustedes",
      "asesor humano",
      "estoy muy molest",
      "queja formal",
      "voy a demandar"
    ] },
    "external": { "enabled": false, "events": {} }
  }
}';

IF ISJSON(@SettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedLuisPetitBarber: SettingsJson invalido.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,
         SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Luis',
         N'Agente de reservas de Luis Petit Profesional Barber con agenda por hora, anticipo del 50% y notificaciones de reserva.',
         1, @SettingsJson, @SystemPrompt, N'gpt-4.1-mini', 0.66, 8, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET BusinessId = @BusinessId,
        AgentTypeId = @AgentTypeId,
        Name = N'Luis',
        Description = N'Agente de reservas de Luis Petit Profesional Barber con agenda por hora, anticipo del 50% y notificaciones de reserva.',
        IsActive = 1,
        SettingsJson = @SettingsJson,
        SystemPromptMarkdown = @SystemPrompt,
        Model = N'gpt-4.1-mini',
        Temperature = 0.66,
        MaxToolIterations = 8,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END

UPDATE dbo.BusinessWhatsAppNumbers
SET AgentId = @AgentId
WHERE BusinessId = @BusinessId
  AND (AgentId IS NULL OR AgentId <> @AgentId);

IF NOT EXISTS (SELECT 1 FROM dbo.BusinessWhatsAppNumbers WHERE BusinessId = @BusinessId AND IsActive = 1)
BEGIN
    PRINT N'SeedLuisPetitBarber: negocio creado sin numero WhatsApp activo. Configura BusinessWhatsAppNumbers para que Luis responda inbound y pueda enviar notificaciones.';
END

DECLARE @ExistingLuisWompiId UNIQUEIDENTIFIER;
DECLARE @SourceWompiConnectionId UNIQUEIDENTIFIER;

SELECT @ExistingLuisWompiId = IntegrationConnectionId
FROM dbo.IntegrationConnections
WHERE BusinessId = @BusinessId
  AND ConnectionType = 0
  AND Provider = 1
  AND Capability = 1
  AND IsEnabled = 1
  AND NULLIF(SecretsJson, N'{}') IS NOT NULL;

SELECT @SourceWompiConnectionId = IntegrationConnectionId
FROM dbo.IntegrationConnections
WHERE BusinessId = @MimosBusinessId
  AND ConnectionType = 0
  AND Provider = 1
  AND Capability = 1
  AND IsEnabled = 1
  AND NULLIF(SecretsJson, N'{}') IS NOT NULL;

IF @ExistingLuisWompiId IS NULL AND @SourceWompiConnectionId IS NOT NULL
BEGIN
    MERGE dbo.IntegrationConnections AS target
    USING (
        SELECT
            @BusinessId AS BusinessId,
            ConnectionType,
            Provider,
            Capability,
            [Name],
            AccountIdentifier,
            SettingsJson,
            SecretsJson,
            IsEnabled
        FROM dbo.IntegrationConnections
        WHERE IntegrationConnectionId = @SourceWompiConnectionId
    ) AS source
       ON target.BusinessId = source.BusinessId
      AND target.ConnectionType = source.ConnectionType
      AND target.Provider = source.Provider
      AND target.Capability = source.Capability
    WHEN MATCHED THEN
        UPDATE SET
            [Name] = source.[Name],
            AccountIdentifier = source.AccountIdentifier,
            SettingsJson = source.SettingsJson,
            SecretsJson = source.SecretsJson,
            IsEnabled = source.IsEnabled,
            UpdatedAt = GETUTCDATE()
    WHEN NOT MATCHED THEN
        INSERT (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],
                AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
        VALUES (NEWID(), source.BusinessId, source.ConnectionType, source.Provider, source.Capability, source.[Name],
                source.AccountIdentifier, source.SettingsJson, source.SecretsJson, source.IsEnabled, GETUTCDATE());

    PRINT N'SeedLuisPetitBarber: Wompi copiado desde Mimos para Luis Petit Profesional Barber.';
END
ELSE IF @ExistingLuisWompiId IS NOT NULL
BEGIN
    PRINT N'SeedLuisPetitBarber: Wompi propio de Luis Petit Profesional Barber preservado.';
END
ELSE
BEGIN
    PRINT N'SeedLuisPetitBarber: Wompi de Mimos no encontrado; configura pagos para habilitar anticipos.';
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

PRINT N'SeedLuisPetitBarber: negocio, servicios y agente Luis configurados.';
GO
