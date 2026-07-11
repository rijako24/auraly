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
  "temperature": 0.62,
  "historyWindowSize": 24,
  "persona": "Eres Aly, empleada digital de AURALY por WhatsApp. Tu mision es explicar con claridad que hace AURALY, que problemas resuelve y guiar a la persona hasta agendar una demo. Hablas en espanol con tono consultivo, humano, directo y comercial, sin sonar robotica. Responde breve, ordenado y con preguntas utiles para avanzar.",
  "policies": "## PROPUESTA DE VALOR\n\n- AURALY crea empleados digitales configurables que trabajan 24/7 en WhatsApp y canales conversacionales.\n- Resolvemos chats sin responder, tiempos de espera altos, leads sin seguimiento, equipos saturados, agendas manuales, pagos abandonados y falta de trazabilidad comercial.\n- Los empleados digitales pueden explicar servicios, calificar leads, recomendar opciones, resolver preguntas frecuentes, agendar demos o citas, generar pagos, recuperar conversaciones y escalar a humanos con historial completo.\n- Enfatiza beneficios: disponibilidad 24/7, velocidad de respuesta, conversion, consistencia de marca, automatizacion de tareas repetitivas, datos estructurados, historial, medicion de consumo y configuracion por negocio.\n- AURALY no reemplaza al equipo humano: libera tiempo operativo y deja al humano los casos sensibles, estrategicos o de alto valor.\n- Evita prometer resultados exactos. Habla de maximizar ventas y mejorar conversion como objetivo, no como garantia.",
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
    }
  },
  "templates": {
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}*Espacios disponibles para {{date_formatted}}*\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "globalActions": [
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
            "escalation.requested": {},
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
                    "conversation_volume",
                    "service",
                    "desired_date",
                    "desired_time",
                    "payment_method",
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
      "label": "problematica que quiere resolver",
      "type": "string",
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
      "source": "user",
      "scope": "request"
    },
    {
      "key": "conversation_volume",
      "role": "business.volume",
      "label": "volumen de conversaciones",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request"
    },
    {
      "key": "value_explained",
      "role": "sales.value_explained",
      "label": "valor explicado",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "ephemeral",
      "retentionDays": 1
    },
    {
      "key": "service",
      "role": "booking.service",
      "label": "servicio tecnico",
      "type": "string",
      "required": false,
      "source": "system",
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
      "label": "nombre",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "company_name",
      "role": "customer.company",
      "label": "empresa",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer"
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
      "required": true,
      "source": "user",
      "scope": "customer"
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
    },
    {
      "key": "customer_confirmed",
      "role": "confirmation.verbal",
      "label": "confirmacion del cliente",
      "type": "boolean",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 1
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
      "enabled": false,
      "recipients": [],
      "sendMessageSequence": null
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
          "goal": "Identificar el tipo de negocio y el principal proceso o cuello de botella de WhatsApp que la persona quiere mejorar.",
          "advanceWhenFacts": [
            "business_type",
            "pain_point"
          ],
          "conversationGuidance": "Si es el primer turno de una conversacion nueva y no hay mensajes previos del bot, envia la secuencia web_demo_follow_up y termina el turno sin texto libre. Usa esa misma plantilla para cualquier origen del primer contacto: WhatsApp, landing, web, campana o API. Si ya hay historial del bot, no repitas la plantilla: si falta business_type o pain_point, pide solo los datos faltantes en una sola pregunta. Si faltan ambos, pregunta: Que tipo de negocio tienes y que proceso de WhatsApp quieres mejorar primero: responder leads, agendar, vender/cobrar, soporte o seguimiento? No agregues otra pregunta ni pidas datos extra. Cuando el cliente responda, registra business_type y pain_point antes de avanzar.",
          "collect": [
            "business_type",
            "pain_point"
          ]
        },
        {
          "id": "business_context",
          "name": "Contexto del negocio",
          "goal": "Entender el problema, canal principal y volumen aproximado antes de recomendar.",
          "advanceWhenFacts": [
            "main_channel"
          ],
          "conversationGuidance": "No hagas preguntas adicionales de diagnostico. Si el cliente ya entrego algun dato de contexto, registralo. Si falta main_channel, asume WhatsApp porque este flujo agenda una demo de automatizacion conversacional.",
          "collect": [
            "main_channel"
          ]
        },
        {
          "id": "value_explanation",
          "name": "Explicacion de valor",
          "goal": "Explicar que hacemos, servicios y ventajas conectadas al problema del cliente.",
          "advanceWhenFacts": [
            "service"
          ],
          "conversationGuidance": "Consulta servicios oficiales. Explica maximo 4 capacidades relevantes para el pain_point: atencion 24/7, calificacion de leads, agenda, pagos, seguimiento, recuperacion, analytics, handoff humano e integraciones. Conecta cada beneficio con el problema mencionado. Recomienda la demo en vivo de AURALY como siguiente paso. No preguntes si quiere ver horarios, no preguntes ni muestres seleccion de servicio. Fija el servicio tecnico con el texto exacto Demo AURALY. Despues continua a agenda en el mismo turno.",
          "collect": [],
          "signals": [
            {
              "type": "service_selection",
              "description": "Texto con el que el cliente elige o corrige un servicio concreto.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "catalog_get_services_1",
              "operation": "catalog.get_services",
              "arguments": {
                "view": "services",
                "query": "{{user.message}}"
              },
              "onOutcome": {
                "catalog.services_returned": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "service",
                      "value": true
                    }
                  ]
                }
              }
            },
            {
              "id": "resolve_service_selection",
              "operation": "catalog.resolve_service",
              "trigger": "on_signal",
              "signal": "service_selection",
              "arguments": {
                "text": "{{signal.service_selection.value}}"
              },
              "onOutcome": {
                "catalog.service_resolved": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "service": "service"
                      }
                    }
                  ]
                },
                "catalog.service_unchanged": {},
                "catalog.add_on_detected": {},
                "catalog.service_ambiguous": {
                  "response": {
                    "mode": "ask_clarification"
                  }
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification"
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
          "advanceWhenFacts": [],
          "reentryOnFactChanged": [
            "desired_date",
            "desired_time"
          ],
          "conversationGuidance": "La demo en vivo de AURALY es el servicio tecnico por defecto. No preguntes servicio ni muestres seleccion de servicio. Si service no esta registrado, resuelvelo con Demo AURALY. Luego resuelve el tipo de atencion para Demo AURALY. Si falta desired_date, pregunta una sola cosa: Para que fecha te gustaria ver horarios de la demo? No agregues otra pregunta en ese mensaje. Cuando el cliente responda fecha, registra desired_date y valida disponibilidad con Demo AURALY para mostrar horarios disponibles. Cuando el cliente elija hora, registra desired_time y valida disponibilidad con Demo AURALY, fecha y hora. Si esta disponible, deja avanzar.",
          "collect": [
            "availability_checked"
          ],
          "signals": [
            {
              "type": "service_selection",
              "description": "Texto con el que el cliente elige o corrige un servicio concreto.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "reservation_check_availability_1",
              "operation": "reservation.check_availability",
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
                "availability.options_available": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "availability_checked",
                      "value": true
                    }
                  ]
                },
                "availability.requested_time_unavailable": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "availability_checked",
                      "value": true
                    }
                  ]
                },
                "availability.none": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "availability_checked",
                      "value": true
                    }
                  ]
                }
              }
            },
            {
              "id": "resolve_service_selection",
              "operation": "catalog.resolve_service",
              "trigger": "on_signal",
              "signal": "service_selection",
              "arguments": {
                "text": "{{signal.service_selection.value}}"
              },
              "onOutcome": {
                "catalog.service_resolved": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "service": "service"
                      }
                    }
                  ]
                },
                "catalog.service_unchanged": {},
                "catalog.add_on_detected": {},
                "catalog.service_ambiguous": {
                  "response": {
                    "mode": "ask_clarification"
                  }
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification"
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
          "conversationGuidance": "Pide en un solo mensaje solo los datos faltantes: nombre, empresa y correo. El telefono viene del canal. Registra customer_name, company_name y customer_email si los entregan. El correo es obligatorio para agendar porque se usa como destinatario de la invitacion.",
          "collect": [
            "customer_name",
            "company_name",
            "customer_email"
          ]
        },
        {
          "id": "confirmation",
          "name": "Confirmacion de demo",
          "goal": "Confirmar la demo AURALY.",
          "advanceWhenFacts": [
            "customer_confirmed"
          ],
          "conversationGuidance": "Muestra resumen breve: demo, fecha, hora, nombre, empresa, correo y telefono. Pide confirmacion. Cuando el cliente confirme claramente, registra customer_confirmed=true desde la confirmaci?n expl?cita y deja avanzar.",
          "collect": [
            "customer_confirmed"
          ]
        },
        {
          "id": "reservation_creation",
          "name": "Creacion de demo",
          "goal": "Crear la reserva de demo solo despues de confirmacion verbal explicita.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Crea la reserva con customer_confirmed=true usando los datos ya validados. Despues confirma que el equipo AURALY tendra el contexto para la demo. Cierra ahi: no preguntes por recordatorios, informacion adicional, ayuda extra ni siguientes pasos.",
          "collect": [],
          "actions": [
            {
              "id": "reservation_create_1",
              "operation": "reservation.create",
              "arguments": {
                "customer_confirmed": true,
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}",
                "customer_name": "{{fact.customer_name}}",
                "customer_phone": "{{fact.customer_phone}}"
              },
              "onOutcome": {
                "reservation.created": {},
                "reservation.idempotent_replay": {}
              }
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

         1, @SettingsJson, N'gpt-4.1-mini', 0.62, GETUTCDATE());

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

        Temperature = 0.62,

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



PRINT N'SeedAuraly: negocio AURALY, empleado Equipo AURALY y agente Aly configurados.';

GO
