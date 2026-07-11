-- =============================================================================
-- SeedAgenticConfiguration.sql
--
-- Configuracion inicial del agente "Mimi Bot" para el motor agentic
-- (extraccion estructurada + ejecucion deterministica sobre gpt-4.1-mini).
--
-- Crea/actualiza:
--   * AgentType "Vendedor"
--   * BusinessWhatsAppNumbers.AgentId (link del numero al agente)
--
-- Notas de diseno:
--   - Persona, flow, guards, factSchema y policies viven en Agents.SettingsJson.
--   - El catalogo NO se siembra como texto: la operaci?n de cat?logo lo genera desde dbo.Services.
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
DECLARE @AddOnsAttachmentId UNIQUEIDENTIFIER = '6f0f1b27-54df-4d07-9f5d-47bfa66d90e1';
DECLARE @PhotographyAttachmentId UNIQUEIDENTIFIER = 'b44fb8e3-fb9b-4c8a-88b1-5412f9cde011';
IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessAttachmentId = @AddOnsAttachmentId)
BEGIN
    INSERT INTO dbo.BusinessAttachments
        (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
    VALUES
        (@AddOnsAttachmentId, @BusinessId, N'Decoraciones.jpeg', N'image', N'Decoraciones.jpeg', N'Imagen de complementos y decoraciones para planes Baby Spa', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.BusinessAttachments
    SET BusinessId = @BusinessId,
        BlobPath = N'Decoraciones.jpeg',
        MediaType = N'image',
        Filename = N'Decoraciones.jpeg',
        Description = N'Imagen de complementos y decoraciones para planes Baby Spa',
        IsActive = 1
    WHERE BusinessAttachmentId = @AddOnsAttachmentId;
END
IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessAttachmentId = @PhotographyAttachmentId)
BEGIN
    INSERT INTO dbo.BusinessAttachments
        (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
    VALUES
        (@PhotographyAttachmentId, @BusinessId, N'Fotografias.jpeg', N'image', N'Fotografias.jpeg', N'Imagen de complementos de fotografia para planes Baby Spa', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.BusinessAttachments
    SET BusinessId = @BusinessId,
        BlobPath = N'Fotografias.jpeg',
        MediaType = N'image',
        Filename = N'Fotografias.jpeg',
        Description = N'Imagen de complementos de fotografia para planes Baby Spa',
        IsActive = 1
    WHERE BusinessAttachmentId = @PhotographyAttachmentId;
END
-- -- AgentType ----------------------------------------------------------------
DECLARE @AgentTypeId UNIQUEIDENTIFIER;
SELECT @AgentTypeId = AgentTypeId FROM dbo.AgentTypes WHERE Name = N'Vendedor';
IF @AgentTypeId IS NULL
BEGIN
    SET @AgentTypeId = NEWID();
    INSERT INTO dbo.AgentTypes (AgentTypeId, Name, Description, IsActive)
    VALUES (
        @AgentTypeId,
        N'Vendedor',
        N'Agente de ventas y reservas - extracciÃ³n estructurada y ejecuciÃ³n determinÃ­stica del agendamiento.',
        1
    );
END
-- -- Agent configuration (SettingsJson = source of truth) ---------------------
-- NOTA: SettingsJson en este script es la fuente de verdad del agente.
--       Editar aqui (escapar comillas simples: ' -> '').
DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.7,
  "persona": "## ROL E IDENTIDAD\n\nEres **Mimi** de **Mimo''s Baby Spa**.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\n## REGLAS GLOBALES\n\n- Responde siempre en espanol con calidez, claridad y tono profesional.\n- Habla de bienestar y acompanamiento; evita promesas medicas o diagnosticos.\n- Mientras no exista reserva confirmada, evita palabras de confirmacion de reserva.\n- Cancelacion/reagendamiento sin costo con minimo 24 horas de anticipacion.\n- Instagram: @mimosbabyspa.\n\n## APERTURA\n\n- En cada apertura del dia, saluda natural, presentate como Mimi de Mimo''s Baby Spa y da la bienvenida.\n- Usa el nombre del cliente o del bebe si esta disponible.\n- Si el cliente ya pidio algo, usa solo una apertura breve antes de continuar con esa intencion.\n- Despues del saludo, sigue de forma natural con lo que el cliente pidio.\n- No uses saludos largos.",
  "messageSequences": {
    "addons_catalog_image": {
      "messages": [
        {
          "body": "Te comparto las opciones de decoraciones:",
          "attachmentId": "6f0f1b27-54df-4d07-9f5d-47bfa66d90e1"
        },
        {
          "body": "Tambien te comparto las opciones de fotografias:",
          "attachmentId": "b44fb8e3-fb9b-4c8a-88b1-5412f9cde011"
        }
      ]
    },
    "reservation_docs": {
      "messages": [
        {
          "body": "Adjuntamos las indicaciones para tu visita:",
          "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1"
        },
        {
          "body": "Estos son los terminos y condiciones:",
          "attachmentId": "9b2fd590-a2cb-5d8d-a687-493efd0b66a2"
        }
      ]
    },
    "reservation_confirmed": {
      "messages": [
        {
          "body": "Tu reserva ha sido confirmada para el {Date} a las {Time}!"
        },
        {
          "body": "Adjuntamos las indicaciones para tu visita:",
          "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1"
        },
        {
          "body": "Estos son los terminos y condiciones:",
          "attachmentId": "9b2fd590-a2cb-5d8d-a687-493efd0b66a2"
        }
      ]
    },
    "reservation_confirmation_request": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_confirmation_request",
          "language": "es_CO",
          "bodyParameters": [
            "{CustomerName}",
            "{Service}",
            "{Date}",
            "{Time}"
          ],
          "buttons": [
            {
              "id": "reservation_attendance:confirm:{job_id}",
              "title": "Confirmar"
            },
            {
              "id": "reservation_attendance:reschedule:{job_id}",
              "title": "Reprogramar"
            }
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
          "bodyParameters": [
            "{CustomerName}",
            "{Service}",
            "{Date}",
            "{Time}"
          ]
        }
      ]
    },
    "enrollment_confirmed": {
      "messages": [
        {
          "body": "Recibimos tu pago de ${amount} {currency}. Tu inscripcion a {Service} quedo registrada en el horario: {fixed_schedule}."
        },
        {
          "body": "Te enviaremos el formulario de inscripcion por este mismo chat."
        }
      ]
    },
    "payment_slot_taken": {
      "messages": [
        {
          "body": "Recibimos tu pago de ${amount} {currency}. Tu comprobante quedo registrado."
        },
        {
          "body": "Lo sentimos, el horario de las {Time} ya no esta disponible porque otro cliente lo reserve primero. Tu pago esta seguro. Quieres elegir otro horario? Opciones: {slots}."
        }
      ]
    },
    "reservation_attendance_confirmed_reply": {
      "messages": [
        {
          "body": "Muchas gracias, tu cita ha sido confirmada."
        }
      ]
    },
    "reservation_attendance_reschedule_reply": {
      "messages": [
        {
          "body": "Claro, para que dia y hora te gustaria reagendar tu cita?"
        }
      ]
    },
    "reservation_created": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "reservation_created",
          "language": "es_CO",
          "bodyParameters": [
            "{CustomerName}",
            "{Service}",
            "{Date}",
            "{Time}",
            "{Total}"
          ]
        }
      ]
    }
  },
  "webhooks": {
    "wompi": {
      "reservation_created": {
        "sendMessageSequence": "reservation_confirmed"
      },
      "slot_unavailable_after_payment": {
        "sendMessageSequence": "payment_slot_taken"
      },
      "enrollment_paid": {
        "sendMessageSequence": "enrollment_confirmed"
      }
    }
  },
  "notifications": {
    "reservation_created": {
      "enabled": true,
      "recipients": [
        "573042052007"
      ],
      "sendMessageSequence": "reservation_created"
    }
  },
  "reservationAutomations": {
    "confirmation": {
      "enabled": true,
      "trigger": {
        "type": "relative",
        "hoursBefore": 24
      },
      "sendMessageSequence": "reservation_confirmation_request",
      "actions": {
        "confirm": {
          "operation": "reservation.manage",
          "arguments": {
            "action": "confirm_attendance",
            "customer_confirmed": true,
            "job_id": "{source_id}"
          },
          "sendMessageSequence": "reservation_attendance_confirmed_reply"
        },
        "reschedule": {
          "operation": "reservation.manage",
          "arguments": {
            "action": "request_reschedule",
            "job_id": "{source_id}"
          },
          "sendMessageSequence": "reservation_attendance_reschedule_reply"
        }
      }
    },
    "reminder": {
      "enabled": true,
      "trigger": {
        "type": "fixedLocalTime",
        "daysBefore": 0,
        "time": "08:00"
      },
      "sendMessageSequence": "reservation_reminder"
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {
      "reservation": {
        "paymentMethods": {
          "transferencia": {
            "label": "transferencia con link de pago",
            "aliases": [
              "transferencia",
              "link de pago"
            ],
            "payment": {
              "percentage": 50
            },
            "template": "checkout_with_deposit",
            "confirmationOutcome": "reservation_created"
          }
        }
      },
      "enrollment": {
        "paymentMethods": {
          "transferencia": {
            "label": "transferencia con link de pago",
            "aliases": [
              "transferencia",
              "link de pago"
            ],
            "payment": {
              "percentage": 100
            },
            "template": "checkout_enrollment_with_payment",
            "confirmationOutcome": "enrollment_paid"
          }
        }
      }
    }
  },
  "templates": {
    "checkout_enrollment_with_payment": "*Resumen de tu inscripcion*\n- Servicio: {{service_name}}\n- Horario de inscripcion: {{fixed_schedule}}\n{{#each line_items}}\n- {{name}}: ${{price}}\n{{/each}}\n- *TOTAL: ${{total}} {{currency}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nPaga en linea: {{link_url}}\n\nCuando el pago sea confirmado, te enviaremos el formulario de inscripcion.",
    "checkout_with_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}{{checkout_note}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nPara confirmar tu reserva, solicitamos un anticipo del {{deposit_pct}}% del valor del servicio.\n\n*Anticipo:* ${{deposit}} {{currency}}\n\nPaga en linea: {{link_url}}\n\nUna vez confirmado el anticipo, tu reserva quedara asegurada. Estamos para ayudarte!",
    "checkout_no_deposit": "*Resumen de tu reserva*\n- Servicio: {{service_name}}\n- Fecha: {{date_formatted}}\n- Hora: {{time}}\n- Precio servicio: ${{service_price}}\n{{#each addons}}\n- {{name}}: ${{price}}{{checkout_note}}\n{{/each}}\n- *TOTAL: ${{total}}*\n\n- Nombre del cliente: {{customer_name}}\n- Telefono: {{customer_phone}}\n{{#if baby_age_months}}\n- Edad del bebe: {{baby_age_months}}\n{{/if}}\n{{#if baby_name}}\n- Nombre del bebe: {{baby_name}}\n{{/if}}\n{{#if baby_birth_date}}\n- Fecha de nacimiento del bebe: {{baby_birth_date}}\n{{/if}}\n\nConfirmas la reserva con esta informacion?",
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}\n*Espacios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "globalActions": [
    {
      "id": "human_escalation",
      "priority": 1000,
      "goal": "Escalar a una persona cuando el cliente lo pida, este inconforme, necesite cotizacion exacta de servicio variable o la solicitud salga del alcance del bot.",
      "conversationGuidance": "Detecta ?nicamente solicitudes expl?citas de atenci?n humana o situaciones configuradas que requieren intervenci?n.",
      "signal": {
        "type": "human_escalation",
        "description": "Solicitud expl?cita de hablar con una persona, inconformidad que requiere intervenci?n o caso fuera del alcance configurado.",
        "valueSchema": {
          "type": "boolean"
        }
      },
      "actions": [
        {
          "id": "request_human",
          "operation": "escalation.request_human",
          "trigger": "on_signal",
          "signal": "human_escalation",
          "arguments": {
            "reason": "{{turn.message}}",
            "last_user_message": "{{turn.message}}"
          },
          "onOutcome": {
            "escalation.requested": {
              "effects": [
                {
                  "type": "escalation.human",
                  "reason": "customer_request"
                }
              ],
              "response": {
                "mode": "deterministic",
                "guidance": "Informa brevemente que ser? atendido por una persona."
              }
            },
            "escalation.notification_failed": {
              "response": {
                "mode": "deterministic",
                "guidance": "Informa que registrar?s la solicitud para atenci?n humana sin prometer un tiempo exacto."
              }
            }
          }
        }
      ]
    }
  ],
  "factSchema": [
    {
      "key": "baby_name",
      "role": "baby.name",
      "label": "nombre del bebe",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "baby_age_months",
      "role": "baby.age_months",
      "label": "edad del bebe (meses)",
      "type": "number",
      "extractionGuidance": "Este fact representa la edad objetivo relevante para recomendar, informar o prestar el servicio, no necesariamente la edad vigente hoy. Si aparecen una edad actual y una futura y el cliente pide informaciÃ³n, recomendaciÃ³n o reserva para la edad futura, guarda Ãºnicamente la edad futura. Si dice que el bebÃ© cumplirÃ¡ N meses prÃ³ximamente en contexto de cumplemes, recomendaciÃ³n o reserva, guarda N. Solo si no puede identificarse quÃ© edad corresponde al servicio, no extraigas el fact y pide aclaraciÃ³n.",
      "required": true,
      "source": "user",
      "scope": "customer",
      "retentionDays": 7
    },
    {
      "key": "baby_birth_date",
      "role": "baby.birth_date",
      "label": "fecha de nacimiento del bebe",
      "type": "date",
      "required": false,
      "source": "user",
      "scope": "customer"
    },
    {
      "key": "service",
      "role": "booking.service",
      "label": "plan / servicio",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "valueSource": "catalog",
      "expireOnBusinessDayChange": true
    },
    {
      "key": "add_ons",
      "role": "booking.addons",
      "label": "complementos",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "dependsOn": [
        "service"
      ],
      "valueSource": "catalog",
      "expireOnBusinessDayChange": true
    },
    {
      "key": "desired_date",
      "role": "booking.date",
      "label": "fecha deseada",
      "type": "date",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true
    },
    {
      "key": "desired_time",
      "role": "booking.time",
      "label": "hora deseada",
      "type": "time",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "expireOnBusinessDayChange": true
    },
    {
      "key": "fixed_schedule_label",
      "role": "checkout.fixed_schedule",
      "label": "horario de inscripcion",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 7,
      "dependsOn": [
        "service"
      ]
    },
    {
      "key": "fulfillment_ready",
      "role": "checkout.fulfillment_ready",
      "label": "ruta de cumplimiento resuelta",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "ephemeral",
      "retentionDays": 1,
      "dependsOn": [
        "service"
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
      "label": "telefono del cliente",
      "type": "phone",
      "required": true,
      "source": "channel",
      "scope": "customer"
    },
    {
      "key": "customer_email",
      "role": "customer.email",
      "label": "email del cliente",
      "type": "email",
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
      "label": "confirmacion verbal del cliente",
      "type": "boolean",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 1,
      "dependsOn": [
        "service",
        "add_ons",
        "desired_date",
        "desired_time",
        "customer_name",
        "baby_birth_date",
        "fixed_schedule_label"
      ]
    }
  ],
  "escalations": {
    "human": {
      "contacts": [
        "+573012926660"
      ]
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "reservationManagement": {
    "automaticChangeFields": [
      "date",
      "time"
    ],
    "escalateChangeFields": [
      "service",
      "add_ons"
    ],
    "escalationReasonCode": "reservation_change_requires_human",
    "manageableReservationGuidance": "Cuando el cliente pida cambiar, cancelar o confirmar una reserva sin identificar una reserva por fecha, hora o servicio existente, pide que indique cual reserva. No infieras disponibilidad ni apliques cambios sobre una reserva no identificada."
  },
  "flows": [
    {
      "stages": [
        {
          "id": "discovery",
          "name": "Conocer al bebÃ©",
          "goal": "Obtener nombre y edad objetivo relevante del bebÃ© para personalizar la recomendaciÃ³n.",
          "advanceWhenFacts": [
            "baby_name",
            "baby_age_months"
          ],
          "collect": [
            "baby_name",
            "baby_age_months",
            "service",
            "add_ons",
            "desired_date",
            "desired_time",
            "customer_name",
            "baby_birth_date",
            "customer_email"
          ],
          "signals": [
            {
              "type": "service_selection",
              "description": "Texto con el que el cliente elige o intenta elegir un plan o servicio concreto.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "resolve_early_service",
              "operation": "catalog.resolve_service",
              "trigger": "on_signal",
              "signal": "service_selection",
              "condition": {
                "factMissing": "service"
              },
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
                "catalog.service_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Presenta Ãºnicamente los candidatos devueltos y pregunta cuÃ¡l servicio desea."
                  }
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que no se encontrÃ³ ese plan o servicio y continÃºa con el catÃ¡logo oficial."
                  }
                }
              }
            }
          ],
          "conversationGuidance": "En la apertura pide Ãºnicamente los datos que falten para avanzar: nombre del bebÃ© y edad objetivo relevante para la recomendaciÃ³n o servicio. Captura tambiÃ©n cualquier otro dato que el cliente dÃ© por adelantado. Si menciona una edad actual y otra futura, aplica extractionGuidance de baby_age_months."
        },
        {
          "id": "service_selection",
          "name": "SelecciÃ³n de servicio",
          "goal": "Ayudar a elegir y resolver un servicio exacto del catÃ¡logo.",
          "advanceWhenFacts": [
            "service"
          ],
          "collect": [
            "service",
            "add_ons",
            "desired_date",
            "desired_time",
            "customer_name",
            "baby_birth_date",
            "customer_email"
          ],
          "signals": [
            {
              "type": "catalog_query",
              "description": "El cliente pide categorÃ­as, planes, servicios, precios o informaciÃ³n del catÃ¡logo.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "service_selection",
              "description": "Texto con el que el cliente elige o intenta elegir un plan o servicio concreto.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "show_catalog_on_entry",
              "operation": "catalog.get_services",
              "trigger": "on_enter",
              "condition": {
                "all": [
                  {
                    "factMissing": "service"
                  },
                  {
                    "not": {
                      "signalPresent": "catalog_query"
                    }
                  },
                  {
                    "not": {
                      "signalPresent": "service_selection"
                    }
                  }
                ]
              },
              "arguments": {
                "query": "{{turn.message}}",
                "view": "categories"
              },
              "onOutcome": {
                "catalog.services_returned": {
                  "response": {
                    "guidance": "Presenta primero las categorÃ­as reales y pregunta cuÃ¡l experiencia le interesa. Usa solo el catÃ¡logo devuelto."
                  }
                }
              }
            },
            {
              "id": "answer_catalog_query",
              "operation": "catalog.get_services",
              "trigger": "on_signal",
              "signal": "catalog_query",
              "arguments": {
                "query": "{{signal.catalog_query.value}}",
                "view": "auto"
              },
              "onOutcome": {
                "catalog.services_returned": {
                  "response": {
                    "guidance": "Responde exclusivamente con las categorÃ­as, servicios, precios y horarios devueltos por el catÃ¡logo."
                  }
                }
              }
            },
            {
              "id": "resolve_service",
              "operation": "catalog.resolve_service",
              "trigger": "on_signal",
              "signal": "service_selection",
              "condition": {
                "factMissing": "service"
              },
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
                "catalog.service_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Presenta solo los candidatos devueltos y pregunta cuÃ¡l servicio desea."
                  }
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que no se encontrÃ³ esa opciÃ³n y ofrece el catÃ¡logo oficial."
                  }
                }
              }
            },
            {
              "id": "resolve_service_fulfillment",
              "operation": "catalog.get_service_fulfillment",
              "condition": {
                "factMissing": "fulfillment_ready"
              },
              "arguments": {
                "service": "{{fact.service}}"
              },
              "onOutcome": {
                "catalog.fulfillment_reservation": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "fulfillment_ready": "fulfillmentReady"
                      }
                    },
                    {
                      "type": "facts.clear",
                      "facts": [
                        "fixed_schedule_label"
                      ]
                    }
                  ]
                },
                "catalog.fulfillment_enrollment": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "fulfillment_ready": "fulfillmentReady",
                        "fixed_schedule_label": "fixedScheduleLabel"
                      }
                    }
                  ]
                },
                "catalog.fulfillment_missing_schedule": {
                  "effects": [
                    {
                      "type": "escalation.human",
                      "reason": "service_fulfillment_missing_schedule"
                    }
                  ]
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide que elija nuevamente un servicio del catÃ¡logo vigente."
                  }
                }
              }
            }
          ],
          "conversationGuidance": "Explica categorÃ­as y servicios usando Ãºnicamente el outcome vigente del catÃ¡logo. No confirmes un servicio hasta que la resoluciÃ³n lo guarde de forma canÃ³nica."
        },
        {
          "id": "addons_offering",
          "name": "Complementos",
          "goal": "Resolver complementos del servicio elegido usando el catÃ¡logo vigente.",
          "advanceWhenFacts": [
            "add_ons"
          ],
          "collect": [
            "add_ons"
          ],
          "signals": [
            {
              "type": "catalog_selection",
              "description": "El cliente elige, rechaza o corrige un servicio o complemento del catÃ¡logo.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "resolve_catalog_selection",
              "operation": "catalog.resolve_service",
              "trigger": "on_signal",
              "signal": "catalog_selection",
              "arguments": {
                "text": "{{signal.catalog_selection.value}}"
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
                "catalog.add_on_detected": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "add_ons": "addOns"
                      }
                    }
                  ]
                },
                "catalog.service_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Aclara cuÃ¡l plan, servicio o complemento desea."
                  }
                },
                "catalog.service_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Aclara cuÃ¡l complemento del catÃ¡logo desea."
                  }
                }
              }
            },
            {
              "id": "get_compatible_add_ons",
              "operation": "catalog.get_compatible_add_ons",
              "condition": {
                "factMissing": "add_ons"
              },
              "arguments": {
                "service": "{{fact.service}}"
              },
              "onOutcome": {
                "catalog.add_ons_available": {
                  "response": {
                    "guidance": "Presenta solo los complementos compatibles devueltos y pregunta cuÃ¡l desea o si continÃºa sin complementos."
                  }
                },
                "catalog.no_add_ons": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "add_ons",
                      "value": "ninguno"
                    }
                  ]
                }
              }
            }
          ],
          "conversationGuidance": "Si el cliente rechaza complementos claramente, guarda add_ons=ninguno. No inventes decoraciones, fotografÃ­as ni precios que no estÃ©n en el outcome vigente."
        },
        {
          "id": "scheduling",
          "name": "Agenda",
          "goal": "Revisar disponibilidad y validar fecha y hora para una reserva por hora.",
          "collect": [
            "desired_date",
            "desired_time"
          ],
          "actions": [
            {
              "id": "check_availability",
              "operation": "reservation.check_availability",
              "condition": {
                "all": [
                  {
                    "verificationMissing": "availability_checked"
                  },
                  {
                    "factMissing": "fixed_schedule_label"
                  }
                ]
              },
              "arguments": {
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}"
              },
              "onOutcome": {
                "availability.exact_time_available": {
                  "response": {
                    "guidance": "Confirma brevemente que el horario estÃ¡ disponible y continÃºa con los datos faltantes."
                  }
                },
                "availability.options_available": {
                  "response": {
                    "mode": "continue"
                  }
                },
                "availability.requested_time_unavailable": {
                  "response": {
                    "mode": "ask_clarification"
                  }
                },
                "availability.none": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que no hay espacios ese dÃ­a y pregunta por otra fecha."
                  }
                },
                "input.invalid_date": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide una fecha vÃ¡lida."
                  }
                },
                "input.past_date": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide una fecha de hoy en adelante."
                  }
                },
                "input.invalid_time": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide una hora vÃ¡lida."
                  }
                },
                "catalog.service_unresolved": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide que elija nuevamente un servicio del catÃ¡logo vigente."
                  }
                }
              }
            }
          ],
          "transitions": [
            {
              "id": "fixed_schedule_selected",
              "priority": 20,
              "condition": {
                "factPresent": "fixed_schedule_label"
              },
              "to": "customer_data"
            },
            {
              "id": "availability_verified",
              "priority": 10,
              "condition": {
                "verificationActive": "availability_checked"
              },
              "to": "customer_data"
            }
          ],
          "conversationGuidance": "Si existe fixed_schedule_label, continÃºa sin consultar disponibilidad porque el servicio usa horario oficial de inscripciÃ³n. Para reservas por hora, si falta fecha pregunta el dÃ­a; con fecha y sin hora, la operaciÃ³n configurada muestra slots mediante template exclusivo; con hora exacta valida ese horario. No afirmes disponibilidad sin outcome vigente."
        },
        {
          "id": "customer_data",
          "name": "Datos del cliente",
          "goal": "Obten el nombre del cliente (papa o mama) y la fecha de nacimiento del bebe.",
          "advanceWhenFacts": [
            "customer_name",
            "baby_birth_date"
          ],
          "conversationGuidance": "Confirma brevemente la seleccion ya definida: fecha y hora agendada, u horario oficial de inscripcion si aplica. Pide juntos los datos que falten para el registro: nombre de la persona que hace el registro y fecha de nacimiento del bebe. Si uno de esos datos ya esta en ESTADO ACTUAL, pide solo el que falta.",
          "collect": [
            "customer_name",
            "baby_birth_date",
            "customer_email"
          ]
        },
        {
          "id": "finalization",
          "name": "Cierre con anticipo",
          "goal": "Preparar el resumen, generar el link de anticipo y esperar confirmacion automatica de pago.",
          "actions": [
            {
              "id": "prepare_authoritative_checkout",
              "operation": "reservation.prepare_checkout",
              "condition": {
                "all": [
                  {
                    "factPresent": "service"
                  },
                  {
                    "factPresent": "add_ons"
                  },
                  {
                    "factPresent": "customer_name"
                  },
                  {
                    "factPresent": "customer_phone"
                  },
                  {
                    "factPresent": "baby_birth_date"
                  },
                  {
                    "factPresent": "fulfillment_ready"
                  },
                  {
                    "verificationMissing": "checkout_prepared"
                  }
                ],
                "any": [
                  {
                    "verificationActive": "availability_checked"
                  },
                  {
                    "factPresent": "fixed_schedule_label"
                  }
                ]
              },
              "arguments": {
                "service": "{{fact.service}}",
                "add_ons": "{{fact.add_ons}}",
                "context": {
                  "date": "{{fact.desired_date}}",
                  "time": "{{fact.desired_time}}",
                  "fixed_schedule": "{{fact.fixed_schedule_label}}",
                  "customer_name": "{{fact.customer_name}}",
                  "customer_phone": "{{fact.customer_phone}}",
                  "baby_name": "{{fact.baby_name}}",
                  "baby_age_months": "{{fact.baby_age_months}}",
                  "baby_birth_date": "{{fact.baby_birth_date}}"
                }
              }
            },
            {
              "id": "create_confirmed_no_payment_reservation",
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
                    "verificationActive": "checkout_no_payment_prepared"
                  }
                ]
              },
              "arguments": {
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}",
                "customer_name": "{{fact.customer_name}}",
                "customer_phone": "{{fact.customer_phone}}",
                "customer_email": "{{fact.customer_email}}",
                "add_ons": "{{fact.add_ons}}",
                "customer_confirmed": true
              },
              "onOutcome": {
                "reservation.created": {
                  "effects": [
                    {
                      "type": "request.complete"
                    }
                  ]
                }
              }
            }
          ],
          "transitions": [
            {
              "id": "revalidate_changed_schedule",
              "priority": 100,
              "condition": {
                "all": [
                  {
                    "verificationMissing": "availability_checked"
                  },
                  {
                    "factMissing": "fixed_schedule_label"
                  }
                ]
              },
              "to": "scheduling"
            }
          ],
          "advanceWhenFacts": [],
          "conversationGuidance": "El motor prepara y presenta el checkout autoritativo cuando los datos y verificaciones configurados estan listos. No reconstruyas resumenes ni links desde el historial. Si cambia servicio, complementos, fecha u hora, usa solo los facts vigentes y espera la nueva validacion deterministica. Antes de pago aprobado o reserva creada, habla de solicitud o link pendiente. Para checkout sin pago, solicita confirmacion verbal antes de crear la reserva.",
          "collect": [
            "service",
            "add_ons",
            "desired_date",
            "desired_time",
            "customer_name",
            "customer_phone",
            "baby_name",
            "baby_age_months",
            "baby_birth_date",
            "customer_confirmed"
          ]
        }
      ],
      "id": "booking",
      "type": "primary",
      "routingGuidance": "Use this primary flow for new Baby Spa bookings, catalog questions, service selection, add-ons, scheduling, customer data and checkout summaries."
    },
    {
      "id": "reservation_management",
      "type": "secondary",
      "ttlSeconds": 900,
      "routingGuidance": "Use only when the customer clearly wants to manage an existing reservation: view it, confirm attendance, cancel it, or change its date, time, service or add-ons. Do not use it for an open booking request, a pending checkout summary or a pending payment link.",
      "stages": [
        {
          "id": "reservation_management",
          "name": "Gestion de reserva existente",
          "goal": "Gestionar una reserva existente sin mezclarla con una solicitud nueva.",
          "collect": [
            "desired_date",
            "desired_time"
          ],
          "signals": [
            {
              "type": "reservation_management_request",
              "description": "Solicitud explÃ­cita para consultar, cambiar, confirmar o cancelar una reserva existente. Usa apply_change solo cuando el cliente pide aplicar el cambio; no inventes reservation_id.",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "action": {
                    "type": "string",
                    "enum": [
                      "request_reschedule",
                      "preview_change",
                      "apply_change",
                      "confirm_attendance",
                      "cancel"
                    ]
                  },
                  "reservation_id": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "payment_transaction_id": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "job_id": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "service": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "date": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "time": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "add_ons": {
                    "type": [
                      "string",
                      "null"
                    ]
                  },
                  "add_ons_mode": {
                    "type": [
                      "string",
                      "null"
                    ],
                    "enum": [
                      "add",
                      "remove",
                      "replace",
                      null
                    ]
                  },
                  "customer_confirmed": {
                    "type": [
                      "boolean",
                      "null"
                    ]
                  },
                  "notes": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "action",
                  "reservation_id",
                  "payment_transaction_id",
                  "job_id",
                  "service",
                  "date",
                  "time",
                  "add_ons",
                  "add_ons_mode",
                  "customer_confirmed",
                  "notes"
                ]
              }
            }
          ],
          "actions": [
            {
              "id": "list_reservations_on_entry",
              "operation": "reservation.list",
              "trigger": "on_enter",
              "arguments": {},
              "onOutcome": {
                "reservation.listed": {
                  "response": {
                    "guidance": "Usa Ãºnicamente las reservas devueltas para identificar la solicitud del cliente; nunca pidas UUID."
                  }
                }
              }
            },
            {
              "id": "manage_reservation_request",
              "operation": "reservation.manage",
              "trigger": "on_signal",
              "signal": "reservation_management_request",
              "arguments": {
                "action": "{{signal.reservation_management_request.value.action}}",
                "reservation_id": "{{signal.reservation_management_request.value.reservation_id}}",
                "payment_transaction_id": "{{signal.reservation_management_request.value.payment_transaction_id}}",
                "job_id": "{{signal.reservation_management_request.value.job_id}}",
                "service": "{{signal.reservation_management_request.value.service}}",
                "date": "{{signal.reservation_management_request.value.date}}",
                "time": "{{signal.reservation_management_request.value.time}}",
                "add_ons": "{{signal.reservation_management_request.value.add_ons}}",
                "add_ons_mode": "{{signal.reservation_management_request.value.add_ons_mode}}",
                "customer_confirmed": "{{signal.reservation_management_request.value.customer_confirmed}}",
                "notes": "{{signal.reservation_management_request.value.notes}}"
              },
              "onOutcome": {
                "reservation.managed": {
                  "response": {
                    "guidance": "Comunica Ãºnicamente el resultado devuelto por la operaciÃ³n de gestiÃ³n de reserva."
                  }
                }
              }
            }
          ],
          "conversationGuidance": "Este flow solo aplica a reservas existentes. Si el cliente pide cambiar fecha u hora de una reserva existente, el motor valida la disponibilidad y aplica el cambio con el nuevo dato si corresponde. Si pide cambiar servicio o adicionales de una reserva ya confirmada, el motor decide si coloca la reserva en espera y escala. Si hay varias reservas, usa Ãºnicamente las reservas vigentes devueltas por el motor o pide que la identifique por fecha, hora o servicio; nunca pidas UUID al cliente. No generes checkout nuevo para cambios de una reserva ya pagada. Si el cliente empieza una solicitud nueva, deja que el router vuelva al flow principal."
        }
      ]
    }
  ]
}';
-- -- Agent (Mimi Bot) ---------------------------------------------------------
DECLARE @AgentId UNIQUEIDENTIFIER;
SELECT @AgentId = AgentId
FROM dbo.Agents
WHERE BusinessId = @BusinessId AND Name IN (N'Mimo Bot', N'Mimi Bot');
IF @AgentId IS NULL
BEGIN
    SET @AgentId = NEWID();
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, Name, Description, IsActive,
         SettingsJson, Model, Temperature)
    VALUES (
        @AgentId,
        @BusinessId,
        @AgentTypeId,
        N'Mimi Bot',
        N'Agente principal de Mimo''s Baby Spa: reservas, pagos y atencion al cliente.',
        1,
        @SettingsJson, N'gpt-4.1-mini', 0.7
    );
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET Name                  = N'Mimi Bot',
        SettingsJson          = @SettingsJson,
        Model                 = N'gpt-4.1-mini',
        Temperature           = 0.7,
        IsActive              = 1,
        UpdatedAt             = SYSUTCDATETIME()
    WHERE AgentId = @AgentId;
END
-- -- Vincular WhatsApp del negocio al agente ----------------------------------
UPDATE dbo.BusinessWhatsAppNumbers
SET AgentId = @AgentId,
    WhatsAppBusinessAccountId = COALESCE(NULLIF(WhatsAppBusinessAccountId, N''), N'2562841327443156')
WHERE BusinessId = @BusinessId
  AND (AgentId IS NULL OR AgentId <> @AgentId OR WhatsAppBusinessAccountId IS NULL OR LTRIM(RTRIM(WhatsAppBusinessAccountId)) = N'');
PRINT N'SeedAgenticConfiguration: Mimi Bot configured for business ' + CAST(@BusinessId AS NVARCHAR(36));
GO
