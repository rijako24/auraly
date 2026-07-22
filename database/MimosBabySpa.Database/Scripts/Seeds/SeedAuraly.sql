-- =============================================================================

-- SeedAuraly.sql

--

-- Crea/actualiza el negocio AURALY, el empleado Equipo AURALY y el agente

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

         N'Remoto', N'+573117324418', N'admin@auraly.ai', N'https://auraly.ai', 1, GETUTCDATE());

END

ELSE

BEGIN

    UPDATE dbo.Businesses

    SET TenantId = @TenantId,

        Name = N'AURALY',

        Description = N'Plataforma de empleados digitales configurables para WhatsApp, ventas, agenda, soporte, pagos y seguimiento comercial 24/7.',

        Address = N'Remoto',

        Phone = N'+573117324418',

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

    VALUES (@EmployeeId, @BusinessId, N'Equipo AURALY', 1, GETUTCDATE());

END

ELSE

BEGIN

    UPDATE dbo.Employees

    SET BusinessId = @BusinessId,

        Name = N'Equipo AURALY',

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

(1, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

(2, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

(3, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

(4, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

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



MERGE dbo.EmployeeWorkingHours AS target

USING @Hours AS source

   ON target.BusinessId = @BusinessId

  AND target.EmployeeId = @EmployeeId

  AND target.DayOfWeek = source.DayOfWeek

  AND target.OpenTime = source.OpenTime

WHEN MATCHED THEN

    UPDATE SET CloseTime = source.CloseTime,

               IsActive = 1,

               UpdatedAt = GETUTCDATE()

WHEN NOT MATCHED THEN

    INSERT (EmployeeWorkingHourId, BusinessId, EmployeeId, DayOfWeek, OpenTime, CloseTime, IsActive, CreatedAt)

    VALUES (NEWID(), @BusinessId, @EmployeeId, source.DayOfWeek, source.OpenTime, source.CloseTime, 1, GETUTCDATE());



UPDATE dbo.EmployeeWorkingHours

SET IsActive = 0,

    UpdatedAt = GETUTCDATE()

WHERE BusinessId = @BusinessId

  AND EmployeeId = @EmployeeId

  AND NOT EXISTS (

      SELECT 1

      FROM @Hours h

      WHERE h.DayOfWeek = EmployeeWorkingHours.DayOfWeek

        AND h.OpenTime = EmployeeWorkingHours.OpenTime

  );



SELECT @AgentTypeId = AgentTypeId FROM dbo.AgentTypes WHERE Name = N'Vendedor';



IF @AgentTypeId IS NULL

BEGIN

    SET @AgentTypeId = NEWID();

    INSERT INTO dbo.AgentTypes (AgentTypeId, Name, Description, IsActive)

    VALUES (@AgentTypeId, N'Vendedor', N'Agente de ventas y agendamiento.', 1);

END






DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "historyWindowSize": 24,
  "extractorHistoryWindowSize": 2,
  "persona": "Eres Aly, el bot de AURALY por WhatsApp. Tu mision es explicar con claridad que hace AURALY, que problemas resuelve y guiar a la persona hasta agendar una demo. Cuando representes a AURALY, habla como parte del equipo y usa expresiones como podemos ayudarte; reserva puedo para acciones que realizas tu como bot. Hablas en espanol con tono consultivo, humano, directo y comercial, sin sonar robotica. Responde breve, ordenado y con preguntas utiles para avanzar.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n- Si preguntan por precios, costos, planes o tarifas, explica que en la demo se les dara toda la informacion comercial y continua desde el punto actual. No inventes ni anticipes montos.\n\n## PROPUESTA DE VALOR\n\n- AURALY crea empleados digitales configurables que trabajan 24/7 en WhatsApp y canales conversacionales.\n- Resolvemos chats sin responder, tiempos de espera altos, leads sin seguimiento, equipos saturados, agendas manuales, pagos abandonados y falta de trazabilidad comercial.\n- Los empleados digitales pueden explicar servicios, calificar leads, recomendar opciones, resolver preguntas frecuentes, agendar demos o citas, generar pagos, recuperar conversaciones y escalar a humanos con historial completo.\n- Enfatiza beneficios: disponibilidad 24/7, velocidad de respuesta, conversion, consistencia de marca, automatizacion de tareas repetitivas, datos estructurados, historial, medicion de consumo y configuracion por negocio.\n- AURALY no reemplaza al equipo humano: libera tiempo operativo y deja al humano los casos sensibles, estrategicos o de alto valor.\n- Evita prometer resultados exactos. Habla de maximizar ventas y mejorar conversion como objetivo, no como garantia.",
  "conversationOpening": {
    "enabled": true,
    "guidance": "Escribe exactamente este texto, conservando los saltos de linea: \uD83D\uDC4B Hola, soy Aly de AURALY.\n\n\u00A1Un gusto saludarte!\n\nEstoy aqui para darte toda la informacion, contarte como podemos ayudarte y acompanarte a agendar una demo en vivo. No agregues preguntas ni texto adicional.",
    "allowQuestions": false
  },
  "conversationFollowUp": {
    "enabled": true,
    "delayMinutes": 120,
    "guidance": "Retoma con calidez y brevedad la pregunta, dato, fecha, horario o confirmacion concreta que sigue pendiente para agendar la demo. Usa el contexto vigente y formula una sola pregunta enfocada. No repitas la explicacion completa de AURALY ni el resumen de la demo; no agregues urgencia, descuentos, precios, disponibilidad inventada ni promesas, y no crees ni modifiques reservas.",
    "respectOperatingHours": true
  },
  "messageSequences": {
    "web_demo_follow_up": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "auraly_demo_engagement_v2",
          "language": "es_CO",
          "bodyParameters": [
            "{CustomerName}"
          ]
        }
      ]
    },
    "internal_demo_scheduled": {
      "messages": [
        {
          "type": "text",
          "body": "\uD83D\uDCC5 *Nueva demo AURALY agendada*\n\n\u2022 Cliente: {CustomerName}\n\u2022 Empresa: {company_name}\n\u2022 Telefono: {customer_phone}\n\u2022 Correo: {customer_email}\n\u2022 Fecha: {Date}\n\u2022 Hora: {Time}\n\u2022 Tipo de negocio: {business_type}\n\u2022 Quiere automatizar o mejorar: {pain_point}"
        }
      ]
    }
  },
  "templates": {
    "availability_slots": "{{#if intro_message}}{{intro_message}}{{/if}}\n\n*Espacios disponibles para {{date_formatted}}*\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?",
    "discovery_question": "{{#if business_type}}{{#if company_name}}{{#if pain_point}}{{else}}\uD83D\uDCAC Cuentame brevemente:\n\n\u2022 \u00BFQue proceso te gustaria automatizar o mejorar en WhatsApp?\n\u2022 \u00BFComo manejas hoy ese proceso?{{/if}}{{else}}{{#if pain_point}}\uD83C\uDFE2 \u00BFComo se llama tu empresa?{{else}}\uD83C\uDFE2 Cuentame brevemente:\n\n\u2022 \u00BFComo se llama tu empresa?\n\u2022 \u00BFQue proceso te gustaria automatizar o mejorar en WhatsApp?{{/if}}{{/if}}{{else}}{{#if company_name}}{{#if pain_point}}\uD83C\uDFE2 \u00BFQue tipo de negocio tienes?{{else}}\uD83C\uDFE2 Cuentame brevemente:\n\n\u2022 \u00BFQue tipo de negocio tienes?\n\u2022 \u00BFQue proceso te gustaria automatizar o mejorar en WhatsApp?{{/if}}{{else}}{{#if pain_point}}\uD83C\uDFE2 Cuentame brevemente:\n\n\u2022 \u00BFComo se llama tu empresa?\n\u2022 \u00BFQue tipo de negocio tienes?{{else}}\uD83C\uDFE2 Para orientarte mejor, cuentame brevemente:\n\n\u2022 \u00BFComo se llama tu empresa?\n\u2022 \u00BFQue tipo de negocio tienes?\n\u2022 \u00BFQue proceso te gustaria automatizar o mejorar en WhatsApp?{{/if}}{{/if}}{{/if}}",
    "value_explanation": "*\u2728 Asi puede ayudarte AURALY*\n\n\uD83D\uDCAC *Atencion inmediata:* responde 24/7, resuelve consultas y recoge los datos necesarios.\n\n\uD83D\uDCC5 *Agenda conectada:* consulta disponibilidad, ofrece horarios validos y registra citas sin cruces manuales.\n\n\uD83D\uDD14 *Seguimiento automatico:* envia confirmaciones y recordatorios, conserva el historial y entrega los casos especiales a tu equipo con todo el contexto.\n\n{{#if desired_date}}Ya tengo la fecha {{desired_date}}. \u00BFTienes alguna duda o consultamos los horarios para ese dia?{{else}}\u00BFTienes alguna duda o avanzamos con la demo? Si avanzamos, \u00BFpara que fecha deseas ver horarios?{{/if}}",
    "customer_data_question": "*\uD83D\uDCCB Ultimos datos para la demo*\n\n\u2022 Tu nombre\n\u2022 Tu correo para enviarte la invitacion",
    "social_profiles_question": "*\uD83D\uDD17 Antes de mostrarte el resumen*\n\nSi quieres, compartenos los perfiles de Facebook e Instagram de tu empresa. Puedes enviarnos uno, ambos o decirnos que prefieres continuar sin compartirlos.\n\n*Es opcional y nos ayuda a personalizar la demo.*",
    "pricing_demo_information": "\uD83D\uDCB0 En la demo te daremos toda la informacion sobre precios y planes de AURALY, de acuerdo con lo que necesita tu negocio.\n\nPodemos continuar desde donde quedamos.",
    "demo_confirmation": "*Resumen de tu demo AURALY*\n- Fecha: {{desired_date}}\n- Hora: {{desired_time}}\n- Nombre: {{customer_name}}\n- Empresa: {{company_name}}\n- Correo: {{customer_email}}\n- Telefono: {{customer_phone}}\n- Tipo de negocio: {{business_type}}\n- Quiere automatizar o mejorar: {{pain_point}}\n{{#if business_profile_url}}- Facebook/Instagram: {{business_profile_url}}\n{{/if}}\nConfirmas la demo con esta informacion?",
    "demo_created": "Tu demo AURALY quedo agendada para el {{desired_date}} a las {{desired_time}}. El equipo AURALY recibira el contexto que compartiste.",
    "human_handoff_ack": "Voy a transferir tu conversacion al equipo AURALY. Recibiran el contexto que ya compartiste.",
    "past_date_invalid": "La fecha indicada ya paso. Para que fecha de hoy en adelante te gustaria ver horarios de la demo?",
    "date_invalid": "No pude interpretar esa fecha. Indica una fecha valida de hoy en adelante.",
    "time_invalid": "No pude interpretar esa hora. Indica una hora valida para la demo.",
    "availability_none": "No hay espacios disponibles para esa fecha. Que otra fecha te sirve?",
    "booking_already_confirmed": "\u2705 Tu demo ya quedo agendada. No necesitas confirmarla nuevamente."
  },
  "globalActions": [
    {
      "id": "booking_confirmation_replay",
      "priority": 99,
      "goal": "Responder una confirmacion repetida despues de que la demo ya fue creada sin abrir otra solicitud.",
      "conversationGuidance": "Activa solo cuando el mensaje inmediatamente anterior de Aly ya confirmo que la demo quedo agendada y el cliente vuelve a confirmar. No la actives ante el resumen que pregunta si confirma.",
      "signal": {
        "type": "booking_confirmation_replay",
        "description": "El cliente vuelve a confirmar una demo justo despues de que Aly ya dijo que quedo agendada. No aplica cuando Aly apenas mostro el resumen y solicito la primera confirmacion.",
        "valueSchema": {
          "type": "string"
        }
      },
      "actions": [],
      "response": {
        "template": "booking_already_confirmed"
      }
    },
    {
      "id": "pricing_information",
      "priority": 95,
      "goal": "Responder preguntas sobre precios, costos, planes o tarifas sin inventar montos.",
      "conversationGuidance": "Indica que toda la informacion comercial se entrega durante la demo y conserva el punto actual de la conversacion.",
      "signal": {
        "type": "pricing_question",
        "description": "El cliente pregunta por precio, costo, tarifa, plan, valor mensual, valor de implementacion o cuanto cuesta AURALY.",
        "valueSchema": {
          "type": "string"
        }
      },
      "actions": [],
      "response": {
        "template": "pricing_demo_information",
        "awaitCustomerReply": true
      }
    },
    {
      "id": "human_handoff",
      "priority": 100,
      "goal": "Escalar a humano cuando lo pidan o cuando la solicitud sea alianza, soporte sensible, compra enterprise o caso fuera de alcance.",
      "conversationGuidance": "Responde con una frase breve y cordial, resume el contexto y escala a humano.",
      "signal": {
        "type": "human_escalation",
        "description": "Escalar a humano cuando lo pidan o cuando la solicitud sea alianza, soporte sensible, compra enterprise o caso fuera de alcance.",
        "valueSchema": {
          "type": "string"
        }
      },
      "actions": [
        {
          "id": "request_human",
          "operation": "escalation.request_human",
          "trigger": "on_signal",
          "signal": "human_escalation",
          "arguments": {
            "reason": "{{signal.human_escalation.value}}",
            "last_user_message": "{{turn.message}}"
          },
          "onOutcome": {
            "escalation.requested": {
              "response": {
                "template": "human_handoff_ack"
              }
            },
            "escalation.notification_failed": {
              "response": {
                "guidance": "Indica que el equipo continuar? la atenci?n."
              }
            }
          }
        }
      ]
    },
    {
      "id": "restart_demo_flow",
      "priority": 70,
      "goal": "Reiniciar la solicitud si el cliente cambia de tema o quiere empezar de nuevo.",
      "conversationGuidance": "Reinicia la solicitud solo si el cliente lo pide claramente o cambia por completo el objetivo.",
      "signal": {
        "type": "restart_request",
        "description": "Reiniciar la solicitud si el cliente cambia de tema o quiere empezar de nuevo.",
        "valueSchema": {
          "type": "string"
        }
      },
      "actions": [
        {
          "id": "reset_request",
          "operation": "conversation.reset_request",
          "trigger": "on_signal",
          "signal": "restart_request",
          "arguments": {},
          "onOutcome": {
            "conversation.request_reset": {
              "effects": [
                {
                  "type": "facts.clear",
                  "facts": [
                    "pain_point",
                    "business_type",
                    "main_channel",
                    "service",
                    "desired_date",
                    "desired_time",
                    "customer_confirmed"
                  ]
                }
              ]
            }
          }
        }
      ]
    }
  ],
  "factSchema": [
    {
      "key": "pain_point",
      "role": "sales.pain_point",
      "label": "proceso que quiere automatizar o mejorar",
      "type": "string",
      "extractionGuidance": "Conserva una descripcion breve pero completa y fiel de lo que el cliente quiere automatizar o mejorar en WhatsApp, incluyendo el proceso actual, dificultad y objetivo cuando los mencione. No la reduzcas a una categoria cerrada si aporta detalles.",
      "required": true,
      "source": "user",
      "scope": "request",
      "showInCollectedInfo": true
    },
    {
      "key": "business_type",
      "role": "business.type",
      "label": "tipo de negocio",
      "type": "string",
      "extractionGuidance": "Extrae la categoria o sector del negocio, por ejemplo clinica dental o tienda de ropa. No lo actualices con el nombre propio de la empresa.",
      "required": true,
      "source": "user",
      "scope": "request",
      "showInCollectedInfo": true
    },
    {
      "key": "main_channel",
      "role": "business.channel",
      "label": "canal principal",
      "type": "string",
      "required": true,
      "source": "system",
      "defaultValue": "WhatsApp",
      "scope": "request"
    },
    {
      "key": "service",
      "role": "booking.service",
      "label": "servicio tecnico",
      "type": "string",
      "required": false,
      "source": "system",
      "defaultValue": "Demo AURALY",
      "scope": "request"
    },
    {
      "key": "desired_date",
      "role": "booking.date",
      "label": "fecha deseada",
      "type": "date",
      "required": true,
      "source": "user",
      "scope": "request"
    },
    {
      "key": "desired_time",
      "role": "booking.time",
      "label": "hora deseada",
      "type": "time",
      "required": true,
      "source": "user",
      "scope": "request",
      "dependsOn": [
        "service",
        "desired_date"
      ]
    },
    {
      "key": "availability_checked",
      "role": "booking.availability_checked",
      "label": "disponibilidad validada",
      "type": "boolean",
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
      "label": "nombre",
      "type": "string",
      "extractionGuidance": "Extrae el nombre de la persona cuando se presenta con expresiones como soy, me llamo o mi nombre es. No uses el nombre de la empresa.",
      "required": true,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "company_name",
      "role": "customer.company",
      "label": "empresa",
      "type": "string",
      "extractionGuidance": "Extrae el nombre propio de la empresa cuando el cliente lo identifica como empresa, negocio, compania o marca. No lo guardes como business_type.",
      "required": true,
      "source": "user",
      "scope": "customer",
      "showInCollectedInfo": true
    },
    {
      "key": "business_profile_url",
      "role": "business.profile_url",
      "label": "Facebook e Instagram",
      "type": "string",
      "extractionGuidance": "Extrae y conserva juntos los enlaces o usuarios de Facebook e Instagram que el cliente comparta para personalizar la demo. El valor puede contener uno o ambos perfiles. Es opcional y nunca debe bloquear el agendamiento.",
      "required": false,
      "source": "user",
      "scope": "customer",
      "showInCollectedInfo": true
    },
    {
      "key": "social_profiles_answered",
      "role": "conversation.social_profiles_answered",
      "label": "respuesta sobre redes sociales",
      "type": "boolean",
      "extractionGuidance": "Registra true cuando, despues de que se le pidan Facebook e Instagram, el cliente comparte uno o ambos perfiles o indica claramente que prefiere continuar sin compartirlos. No registres false y no lo infieras antes de esa pregunta.",
      "required": false,
      "source": "user",
      "scope": "request"
    },
    {
      "key": "customer_phone",
      "role": "customer.phone",
      "label": "telefono",
      "type": "phone",
      "required": true,
      "source": "channel",
      "scope": "customer"
    },
    {
      "key": "customer_email",
      "role": "customer.email",
      "label": "correo",
      "type": "email",
      "extractionGuidance": "Extrae y normaliza el correo electronico escrito por el cliente.",
      "required": true,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "customer_confirmed",
      "role": "confirmation.verbal",
      "label": "confirmacion del cliente",
      "type": "boolean",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 1,
      "dependsOn": [
        "service",
        "desired_date",
        "desired_time",
        "customer_name",
        "company_name",
        "customer_email"
      ]
    }
  ],
  "escalations": {
    "human": {
      "contacts": [
        "+573117324418"
      ]
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "notifications": {
    "reservation_created": {
      "enabled": true,
      "recipients": [
        "573012926660"
      ],
      "sendMessageSequence": "internal_demo_scheduled"
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {}
  },
  "flows": [
    {
      "id": "booking",
      "type": "primary",
      "routingGuidance": "Use this primary flow for new AURALY demo requests, value explanation, scheduling, customer data and confirmation.",
      "stages": [
        {
          "id": "discovery",
          "name": "Diagnostico inicial",
          "goal": "Identificar el nombre y tipo de negocio y el principal proceso o cuello de botella de WhatsApp que la persona quiere mejorar.",
          "advanceWhenFacts": [
            "company_name",
            "business_type",
            "pain_point"
          ],
          "conversationGuidance": "Pide en un solo mensaje estructurado los datos de descubrimiento que falten: nombre propio de la empresa, tipo de negocio y una explicacion breve y abierta del proceso que quiere automatizar o mejorar en WhatsApp. Si el cliente ya entrego alguno, no lo vuelvas a pedir. No ofrezcas ejemplos ni una lista cerrada de opciones y no pidas datos de agenda todavia. Registra company_name, business_type y una descripcion fiel y suficientemente completa en pain_point antes de avanzar.",
          "collect": [
            "business_type",
            "pain_point",
            "desired_date",
            "desired_time",
            "customer_name",
            "company_name",
            "business_profile_url",
            "customer_email"
          ],
          "response": {
            "template": "discovery_question",
            "awaitCustomerReply": true
          }
        },
        {
          "id": "business_context",
          "name": "Contexto del negocio",
          "goal": "Entender el problema, canal principal y volumen aproximado antes de recomendar.",
          "advanceWhenFacts": [
            "main_channel"
          ],
          "conversationGuidance": "Usa el canal WhatsApp configurado y continua sin hacer preguntas adicionales de diagnostico.",
          "collect": []
        },
        {
          "id": "value_explanation",
          "name": "Explicacion de valor",
          "goal": "Explicar que hacemos, servicios y ventajas conectadas al problema del cliente.",
          "advanceWhenFacts": [
            "service"
          ],
          "conversationGuidance": "Consulta servicios oficiales. Antes de pedir o usar una fecha, explica de manera concreta como AURALY puede automatizar el proceso descrito en pain_point para el business_type indicado. Presenta de 2 a 4 capacidades conectadas causalmente con su caso, incluyendo cuando aplique recepcion 24/7, captura de datos, consulta de disponibilidad real, agendamiento, confirmaciones o recordatorios, seguimiento, pagos, trazabilidad y entrega a un humano con contexto. Explica el flujo que vivirian el cliente y el equipo, no solo una lista generica de beneficios. No prometas resultados exactos. Recomienda la demo AURALY y termina ofreciendo resolver dudas o avanzar. Si desired_date falta, pide en ese mismo cierre que indique para que fecha desea ver horarios; si ya existe, reconoce la fecha y continua sin volver a pedirla. No preguntes servicio ni muestres seleccion de servicio.",
          "collect": [],
          "actions": [
            {
              "id": "catalog_get_services_1",
              "operation": "catalog.get_services",
              "trigger": "on_enter",
              "arguments": {
                "view": "services"
              },
              "onOutcome": {
                "catalog.services_returned": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "service",
                      "value": "Demo AURALY"
                    }
                  ],
                  "response": {
                    "mode": "continue",
                    "template": "value_explanation",
                    "awaitCustomerReply": true
                  }
                }
              }
            }
          ]
        },
        {
          "id": "scheduling",
          "name": "Agenda de demo",
          "goal": "Mostrar disponibilidad y validar fecha/hora para la demo.",
          "advanceWhenFacts": [
            "availability_checked"
          ],
          "conversationGuidance": "La demo en vivo de AURALY es el servicio tecnico por defecto. No preguntes servicio ni muestres seleccion de servicio. Si service no esta registrado, resuelvelo con Demo AURALY. Luego resuelve el tipo de atencion para Demo AURALY. Si falta desired_date, pregunta una sola cosa: Para que fecha te gustaria ver horarios de la demo? No agregues otra pregunta en ese mensaje. Cuando el cliente responda fecha, registra desired_date y valida disponibilidad con Demo AURALY para mostrar horarios disponibles. Cuando el cliente elija hora, registra desired_time y valida disponibilidad con Demo AURALY, fecha y hora. Si esta disponible, deja avanzar.",
          "response": {
            "awaitCustomerReply": true
          },
          "collect": [
            "desired_date",
            "desired_time",
            "customer_name",
            "company_name",
            "business_profile_url",
            "customer_email"
          ],
          "actions": [
            {
              "id": "reservation_check_availability_1",
              "operation": "reservation.check_availability",
              "condition": {
                "not": {
                  "any": [
                    { "factChanged": "business_type" },
                    { "factChanged": "pain_point" }
                  ]
                }
              },
              "arguments": {
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}"
              },
              "onOutcome": {
                "availability.exact_time_available": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "availability_checked",
                      "value": true
                    }
                  ]
                },
                "availability.options_available": {},
                "availability.requested_time_unavailable": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": ["desired_time"]
                    }
                  ]
                },
                "availability.none": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": ["desired_date", "desired_time"]
                    }
                  ],
                  "response": {
                    "mode": "ask_clarification",
                    "template": "availability_none",
                    "awaitCustomerReply": true
                  }
                },
                "input.past_date": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": ["desired_date", "desired_time"]
                    }
                  ],
                  "response": {
                    "mode": "ask_clarification",
                    "template": "past_date_invalid",
                    "awaitCustomerReply": true
                  }
                },
                "input.invalid_date": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": ["desired_date", "desired_time"]
                    }
                  ],
                  "response": {
                    "mode": "ask_clarification",
                    "template": "date_invalid",
                    "awaitCustomerReply": true
                  }
                },
                "input.invalid_time": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": ["desired_time"]
                    }
                  ],
                  "response": {
                    "mode": "ask_clarification",
                    "template": "time_invalid",
                    "awaitCustomerReply": true
                  }
                },
                "input.invalid": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": ["desired_date", "desired_time"]
                    }
                  ],
                  "response": {
                    "mode": "ask_clarification",
                    "template": "date_invalid",
                    "awaitCustomerReply": true
                  }
                },
                "catalog.service_unresolved": {
                  "response": {
                    "guidance": "Indica que no fue posible resolver Demo AURALY y ofrece escalar al equipo. No confirmes disponibilidad.",
                    "awaitCustomerReply": true
                  }
                }
              }
            }
          ],
          "transitions": [
            {
              "id": "availability_verified",
              "priority": 10,
              "condition": {
                "verificationActive": "availability_checked"
              },
              "to": "customer_data"
            }
          ]
        },
        {
          "id": "customer_data",
          "name": "Datos para la demo",
          "goal": "Recoger datos minimos para confirmar la demo.",
          "advanceWhenFacts": [
            "customer_name",
            "company_name",
            "customer_email"
          ],
          "conversationGuidance": "Pide en un solo mensaje solo los datos obligatorios faltantes: nombre, empresa y correo. El telefono viene del canal. Registra customer_name, company_name y customer_email. El correo es obligatorio para agendar porque se usa como destinatario de la invitacion. No pidas redes sociales en esta etapa.",
          "collect": [
            "customer_name",
            "company_name",
            "customer_email",
            "desired_date",
            "desired_time"
          ],
          "response": {
            "template": "customer_data_question",
            "awaitCustomerReply": true
          },
          "transitions": [
            {
              "id": "customer_data_revalidate_availability",
              "priority": 100,
              "condition": {
                "verificationMissing": "availability_checked"
              },
              "to": "scheduling"
            },
            {
              "id": "customer_data_complete",
              "priority": 10,
              "condition": {
                "all": [
                  {
                    "factPresent": "customer_name"
                  },
                  {
                    "factPresent": "company_name"
                  },
                  {
                    "factPresent": "customer_email"
                  }
                ]
              },
              "to": "social_profiles"
            }
          ]
        },
        {
          "id": "social_profiles",
          "name": "Redes para personalizar la demo",
          "goal": "Dar al prospecto la opcion de compartir Facebook e Instagram antes del resumen.",
          "advanceWhenFacts": [
            "social_profiles_answered"
          ],
          "conversationGuidance": "Pide Facebook e Instagram solo en esta etapa, despues de validar fecha y hora y antes del resumen. Aclara que puede compartir uno, ambos o continuar sin compartirlos. Si comparte perfiles, registra business_profile_url y social_profiles_answered=true. Si declina claramente, registra solo social_profiles_answered=true. Nunca bloquees el agendamiento por no compartir redes.",
          "collect": [
            "business_profile_url",
            "social_profiles_answered",
            "desired_date",
            "desired_time"
          ],
          "response": {
            "template": "social_profiles_question",
            "awaitCustomerReply": true
          },
          "transitions": [
            {
              "id": "social_profiles_revalidate_availability",
              "priority": 100,
              "condition": {
                "verificationMissing": "availability_checked"
              },
              "to": "scheduling"
            },
            {
              "id": "social_profiles_answered",
              "priority": 10,
              "condition": {
                "factEquals": {
                  "key": "social_profiles_answered",
                  "value": true
                }
              },
              "to": "confirmation"
            }
          ]
        },
        {
          "id": "confirmation",
          "name": "Confirmacion de demo",
          "goal": "Confirmar la demo AURALY.",
          "advanceWhenFacts": [
            "customer_confirmed"
          ],
          "conversationGuidance": "Presenta el resumen autoritativo y registra customer_confirmed=true solo ante una confirmacion explicita. Una duda, correccion o rechazo no confirma la demo.",
          "collect": [
            "customer_confirmed",
            "desired_date",
            "desired_time",
            "customer_name",
            "company_name",
            "customer_email"
          ],
          "response": {
            "template": "demo_confirmation",
            "awaitCustomerReply": true
          },
          "transitions": [
            {
              "id": "confirmation_revalidate_availability",
              "priority": 100,
              "condition": {
                "verificationMissing": "availability_checked"
              },
              "to": "scheduling"
            },
            {
              "id": "customer_confirmed",
              "priority": 10,
              "condition": {
                "factEquals": {
                  "key": "customer_confirmed",
                  "value": true
                }
              },
              "to": "reservation_creation"
            }
          ]
        },
        {
          "id": "reservation_creation",
          "name": "Creacion de demo",
          "goal": "Crear la reserva de demo solo despues de confirmacion verbal explicita.",
          "advanceWhenFacts": [],
          "conversationGuidance": "La operacion determinista crea la reserva. Confirma la agenda solo con la presentacion de un outcome exitoso y cierra sin preguntas adicionales.",
          "collect": [],
          "actions": [
            {
              "id": "reservation_create_1",
              "operation": "reservation.create",
              "condition": {
                "all": [
                  {
                    "factEquals": {
                      "key": "customer_confirmed",
                      "value": true
                    }
                  },
                  {
                    "verificationActive": "availability_checked"
                  }
                ]
              },
              "arguments": {
                "customer_confirmed": true,
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}",
                "customer_name": "{{fact.customer_name}}",
                "customer_phone": "{{fact.customer_phone}}",
                "customer_email": "{{fact.customer_email}}"
              },
              "execution": {
                "idempotency": "once_per_request",
                "timeoutSeconds": 30,
                "maxAttempts": 1
              },
              "onOutcome": {
                "reservation.created": {
                  "effects": [
                    {
                      "type": "request.complete"
                    }
                  ],
                  "response": {
                    "template": "demo_created"
                  }
                },
                "reservation.idempotent_replay": {
                  "effects": [
                    {
                      "type": "request.complete"
                    }
                  ],
                  "response": {
                    "template": "demo_created"
                  }
                }
              }
            }
          ],
          "transitions": [
            {
              "id": "revalidate_stale_availability",
              "priority": 100,
              "condition": {
                "verificationMissing": "availability_checked"
              },
              "to": "scheduling"
            }
          ]
        }
      ]
    }
  ]
}';



IF ISJSON(@SettingsJson) <> 1

BEGIN

    THROW 51000, 'SeedAuraly: SettingsJson invalido.', 1;

END



IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)

BEGIN

    INSERT INTO dbo.Agents

        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,

         SettingsJson, Model, Temperature, CreatedAt)

    VALUES

        (@AgentId, @BusinessId, @AgentTypeId, N'Aly',

         N'Empleada digital de AURALY para explicar servicios, calificar leads y agendar demos.',

         1, @SettingsJson, N'gpt-4.1-mini', 0.2, GETUTCDATE());

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

        Model = N'gpt-4.1-mini',

        Temperature = 0.2,

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

            Status                 = 1,

            CurrentPeriodStart     = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),

            CurrentPeriodEnd       = DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)),

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

            @BusinessId, SubscriptionPlanId, 1,
            DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),
            DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)),

            PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits,

            MaxVariableCostCop, MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop,

            AutoRenew

        FROM dbo.BusinessSubscriptions

        WHERE BusinessSubscriptionId = @MimosSubscriptionId;

    END

END



PRINT N'SeedAuraly: negocio AURALY, empleado Equipo AURALY y agente Aly configurados.';

GO
