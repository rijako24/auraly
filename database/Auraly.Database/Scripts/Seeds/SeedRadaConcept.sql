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






DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.68,
  "historyWindowSize": 24,
  "persona": "Eres el asistente comercial de Rada Concept por WhatsApp. Atiendes en espanol con tono cercano, elegante y profesional. Ayudas a entender el servicio adecuado y guias hacia una cita de asesoria sin presionar.\n\nResponde claro y breve. Usa listas cortas para explicar servicios, opciones, horarios o resumen de cita.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\n## MARCA\n\n- Rada Concept crea espacios funcionales y esteticos para vivienda, mobiliario, remodelaciones y proyectos comerciales.\n- La cotizacion se define despues de entender el proyecto en una asesoria.\n\n## APERTURA\n\n- En cada apertura del dia, saluda natural, presentate como asistente de Rada Concept y da la bienvenida.\n- Usa el nombre del cliente si esta disponible.\n- Si el cliente ya pidio algo, usa solo una apertura breve antes de continuar con esa intencion.\n- Despues del saludo, sigue de forma natural con lo que el cliente pidio.\n- No uses saludos largos.",
  "templates": {
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}\n*Espacios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "messageSequences": {},
  "globalActions": [
    {
      "id": "human_handoff",
      "priority": 100,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja, solicitud fuera del alcance o necesite cotizacion detallada inmediata.",
      "conversationGuidance": "Responde con una frase breve y cordial, y escala a humano.",
      "signal": {
        "type": "human_escalation",
        "description": "Escalar a humano cuando el cliente lo pida, haya queja, solicitud fuera del alcance o necesite cotizacion detallada inmediata.",
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
      "id": "restart_request",
      "priority": 70,
      "goal": "Reiniciar la solicitud actual cuando el cliente quiera cambiar completamente de servicio o empezar de nuevo.",
      "conversationGuidance": "Reinicia la solicitud cuando el cliente indique claramente que quiere cambiar la solicitud completa. Conserva datos persistentes del cliente.",
      "signal": {
        "type": "restart_request",
        "description": "Reiniciar la solicitud actual cuando el cliente quiera cambiar completamente de servicio o empezar de nuevo.",
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
                    "project_context",
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
      "key": "project_context",
      "role": "project.context",
      "label": "tipo de proyecto o necesidad",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request"
    },
    {
      "key": "service",
      "role": "booking.service",
      "label": "servicio de interes",
      "type": "string",
      "required": true,
      "source": "user",
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
      "label": "nombre del cliente",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "customer_phone",
      "role": "customer.phone",
      "label": "celular de contacto",
      "type": "phone",
      "required": false,
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
      "deliveries": []
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
      "routingGuidance": "Use this primary flow for new Rada Concept design requests, service selection, scheduling, customer data and confirmation.",
      "stages": [
        {
          "id": "discovery",
          "name": "Descubrimiento",
          "goal": "Entender el tipo de servicio de interes.",
          "advanceWhenFacts": [
            "project_context"
          ],
          "conversationGuidance": "Si el mensaje del cliente es solo un saludo, pregunta en que tipo de servicio esta interesado. En ese turno no listes servicios completos de entrada. Si el cliente ya menciona una necesidad, proyecto o servicio, registra project_context cuando aplique y continua a seleccion de servicio.",
          "collect": [
            "project_context"
          ]
        },
        {
          "id": "service_selection",
          "name": "Seleccion de servicio",
          "goal": "Explicar amablemente los servicios de Rada Concept y registrar el servicio de interes.",
          "advanceWhenFacts": [
            "service"
          ],
          "conversationGuidance": "Cuando el cliente responda el tipo de servicio, pida opciones o describa su proyecto, consulta el catalogo oficial. Explica maximo 1 a 3 servicios relevantes con alcance y beneficios, sin mencionar precios. Si pide precio, indica que la cotizacion se define despues de entender medidas, alcance y materiales en asesoria. Cuando el cliente elija un servicio exacto o uno claramente equivalente, registra service con el nombre canonico del catalogo. Si la necesidad puede corresponder a varias opciones, ayuda a escoger con una explicacion breve.",
          "collect": [
            "service"
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
              "id": "catalog_get_services_1",
              "operation": "catalog.get_services",
              "arguments": {
                "view": "auto",
                "query": "{{user.message}}"
              },
              "onOutcome": {
                "catalog.services_returned": {}
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
          "name": "Agenda",
          "goal": "Revisar disponibilidad y acordar fecha y hora para la cita de asesoria.",
          "advanceWhenFacts": [],
          "reentryOnFactChanged": [
            "service",
            "desired_date",
            "desired_time"
          ],
          "conversationGuidance": "Primero resuelve el tipo de atencion con el servicio elegido. Para agenda, pide fecha si falta desired_date. Cuando tengas fecha, valida disponibilidad para mostrar horarios disponibles. Cuando el cliente elija hora, registra desired_time y valida disponibilidad con fecha y hora. Si el horario esta disponible, deja avanzar el flujo.",
          "collect": [
            "availability_checked"
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
          "name": "Datos del cliente",
          "goal": "Recoger datos minimos para crear la cita.",
          "advanceWhenFacts": [
            "customer_name",
            "customer_phone"
          ],
          "conversationGuidance": "Pide en un solo mensaje los datos faltantes para la cita, en lista corta: nombre y celular de contacto. Si ya tienes uno de los datos, pide solo el que falta. Registra los datos entregados.",
          "collect": [
            "customer_name",
            "customer_phone"
          ]
        },
        {
          "id": "confirmation",
          "name": "Confirmacion",
          "goal": "Confirmar la cita de asesoria.",
          "advanceWhenFacts": [
            "customer_confirmed"
          ],
          "conversationGuidance": "Muestra un resumen breve con servicio, fecha, hora y nombre. Pide confirmacion. Cuando el cliente confirme claramente, registra customer_confirmed=true desde la confirmaci?n expl?cita y deja avanzar. Si falta o cambia fecha u hora, vuelve a validar disponibilidad antes de crear la cita.",
          "collect": [
            "customer_confirmed"
          ]
        },
        {
          "id": "reservation_creation",
          "name": "Creacion de cita",
          "goal": "Crear la cita de asesoria solo despues de confirmacion verbal explicita.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Crea la cita con customer_confirmed=true usando los datos ya validados. Despues de crearla, confirma con tono cordial que quedo agendada y cierra sin pedir datos extra.",
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

    THROW 51000, 'SeedRadaConcept: SettingsJson invalido.', 1;

END



IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)

BEGIN

    INSERT INTO dbo.Agents

        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,

         SettingsJson, Model, Temperature, CreatedAt)

    VALUES

        (@AgentId, @BusinessId, @AgentTypeId, N'Asistente Rada Concept',

         N'Asistente comercial para explicar servicios de Rada Concept y agendar citas de asesoria.',

         1, @SettingsJson, N'gpt-4.1-mini', 0.68, GETUTCDATE());

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

        Model = N'gpt-4.1-mini',

        Temperature = 0.68,

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
