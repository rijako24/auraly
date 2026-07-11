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
        N'Agente de ventas y reservas - orquesta el proceso completo de agendamiento via Function Calling.',
        1
    );
END
-- -- Agent configuration (SettingsJson = source of truth) ---------------------
-- NOTA: SettingsJson en este script es la fuente de verdad del agente.
--       Editar aqui (escapar comillas simples: ' -> '').
DECLARE @SystemPrompt NVARCHAR(MAX) = N'';
DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.7,
  "maxToolIterations": 6,
  "consecutiveErrorEscalationThreshold": 3,
  "persona": "## ROL E IDENTIDAD\n\nEres **Mimi** de **Mimo''s Baby Spa**.",
  "policies": "## REGLAS GLOBALES\n\n- Responde siempre en espanol con calidez, claridad y tono profesional.\n- Habla de bienestar y acompanamiento; evita promesas medicas o diagnosticos.\n- Mientras no exista reserva confirmada, evita palabras de confirmacion de reserva.\n- Cancelacion/reagendamiento sin costo con minimo 24 horas de anticipacion.\n- Instagram: @mimosbabyspa.\n\n## APERTURA\n\n- En cada apertura del dia, saluda natural, presentate como Mimi de Mimo''s Baby Spa y da la bienvenida.\n- Usa el nombre del cliente o del bebe si esta disponible.\n- Si el cliente ya pidio algo, usa solo una apertura breve antes de continuar con esa intencion.\n- Despues del saludo, sigue de forma natural con lo que el cliente pidio.\n- No uses saludos largos.",
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
          "tool": "manage_reservation",
          "arguments": {
            "action": "confirm_attendance",
            "customer_confirmed": true,
            "job_id": "{source_id}"
          },
          "sendMessageSequence": "reservation_attendance_confirmed_reply"
        },
        "reschedule": {
          "tool": "manage_reservation",
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
      "allowedActions": [
        "escalate_to_human"
      ],
      "entryActions": [
        {
          "tool": "escalate_to_human",
          "arguments": {
            "reason": "customer_requested_human"
          },
          "when": {
            "messageMatches": [
              {
                "anyOf": [
                  "hablar con una persona",
                  "hablar con un humano",
                  "hablar con humano",
                  "hablar con asesor",
                  "quiero un asesor",
                  "necesito un asesor",
                  "necesito ayuda de una persona",
                  "quiero atencion humana",
                  "pasame con alguien",
                  "comunicarme con una persona"
                ]
              }
            ]
          }
        }
      ],
      "conversationGuidance": "Escala con resumen breve de la necesidad del cliente."
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
      "scope": "ephemeral",
      "expireOnBusinessDayChange": true
    },
    {
      "key": "baby_name",
      "role": "baby.name",
      "label": "nombre del bebe",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "customer",
      "aliases": [
        "nombre bebe",
        "nombre del bebe"
      ]
    },
    {
      "key": "baby_age_months",
      "role": "baby.age_months",
      "label": "edad del bebe (meses)",
      "type": "number",
      "required": true,
      "source": "user",
      "scope": "customer",
      "retentionDays": 7,
      "aliases": [
        "edad",
        "meses",
        "edad bebe"
      ]
    },
    {
      "key": "baby_birth_date",
      "role": "baby.birth_date",
      "label": "fecha de nacimiento del bebe",
      "type": "date",
      "required": false,
      "source": "user",
      "scope": "customer",
      "aliases": [
        "fecha de nacimiento",
        "fecha nacimiento",
        "nacimiento",
        "cuando nacio",
        "cundo nacio"
      ]
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
      "aliases": [
        "fecha",
        "dia",
        "cuando",
        "hoy",
        "manana"
      ],
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
      "aliases": [
        "hora",
        "horario"
      ],
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
      ],
      "aliases": [
        "horario de inscripcion",
        "horario fijo",
        "horario taller"
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
      "scope": "customer",
      "aliases": [
        "nombre",
        "cliente",
        "a nombre de",
        "mi nombre"
      ]
    },
    {
      "key": "customer_phone",
      "role": "customer.phone",
      "label": "telefono del cliente",
      "type": "phone",
      "required": true,
      "source": "channel",
      "scope": "customer",
      "aliases": [
        "telefono",
        "celular",
        "whatsapp",
        "numero"
      ]
    },
    {
      "key": "customer_email",
      "role": "customer.email",
      "label": "email del cliente",
      "type": "email",
      "required": false,
      "source": "user",
      "scope": "customer",
      "aliases": [
        "email",
        "correo"
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
      ],
      "aliases": [
        "confirmo",
        "confirmo reserva",
        "si confirmo",
        "confirmado"
      ]
    }
  ],
  "guards": {
    "capability:reservation.create": {
      "requires": [
        "verification:availability_checked",
        "verification:customer_identified",
        "verification:checkout_no_payment_prepared",
        "state:no_pending_checkout",
        "flag:verbal_confirmation"
      ]
    }
  },
  "enabledTools": [
    "set_fact",
    "get_service_catalog",
    "resolve_service_selection",
    "get_compatible_add_ons",
    "get_service_fulfillment",
    "check_availability",
    "prepare_checkout",
    "create_reservation",
    "get_customer_reservations",
    "manage_reservation",
    "verify_payment",
    "escalate_to_human",
    "reset_flow_context",
    "send_message_sequence"
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
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "discovery",
          "name": "Descubrimiento",
          "goal": "Entender si hay intencion comercial y capturar nombre y edad del bebe.",
          "advanceWhenFacts": [
            "baby_name",
            "baby_age_months"
          ],
          "conversationGuidance": "Si el primer mensaje es solo saludo, agrega solo una pregunta simple de ayuda: En que puedo ayudarte hoy con el bienestar de tu bebe? Si el primer mensaje tambien trae informacion o una intencion, no agregues una pregunta generica; captura todos los datos dados por el cliente antes de responder. En discovery puedes capturar datos aunque no sean necesarios para avanzar: nombre y edad del bebe, servicio, complementos, fecha, hora, nombre del cliente, fecha de nacimiento del bebe o email. Si menciona un plan o servicio, usa resolve_service_selection con el texto literal del cliente; no uses set_fact para service. Para otros datos dados por el cliente, usa set_fact. Avanza cuando esten baby_name y baby_age_months; los demas datos capturados solo preparan las siguientes etapas. Cuando la intencion sea reserva nueva o recomendacion personalizada, pide solo el dato faltante necesario para avanzar.",
          "allowedActions": [
            "resolve_service_selection",
            "set_fact"
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
          ]
        },
        {
          "id": "service_selection",
          "name": "Seleccion de servicio",
          "goal": "Ayudar al cliente a elegir primero una experiencia y luego un servicio exacto del catalogo.",
          "afterTool": [
            {
              "tool": "get_service_fulfillment",
              "when": {
                "path": "data.fulfillment_ready"
              },
              "setFacts": {
                "fulfillment_ready": "{{data.fulfillment_ready}}",
                "fixed_schedule_label": "{{data.fixed_schedule_label}}"
              }
            }
          ],
          "advanceWhenFacts": [
            "service",
            "fulfillment_ready"
          ],
          "conversationGuidance": "Si el cliente ya menciono un plan o servicio exacto en el ultimo mensaje o en el mensaje que llevo a esta etapa, usa resolve_service_selection con el texto literal del cliente antes de responder; no confirmes el servicio ni presentes complementos hasta que la herramienta lo deje canonico en state. Si el mismo mensaje trae otros datos utiles, capturalos tambien con set_fact. Al entrar en esta etapa, consulta el catalogo oficial antes de presentar categorias, servicios, precios u horarios. Con el catalogo retornado, presenta primero solo las categorias reales, pero explicalas con mas sustancia: para cada categoria resume que tipo de experiencia es, cuando conviene elegirla, y 1 beneficio concreto para el bebe segun su edad/etapa si esta disponible. Usa las descripciones oficiales del catalogo como fuente; si una descripcion viene vacia, infiere solo desde los servicios de esa categoria y evita frases genericas como experiencias completas, sesiones especificas o espacios para aprender sin ejemplos. Para un bebe de 5 meses, menciona beneficios esperables como relajacion, estimulacion sensorial suave, vinculo, rutinas o acompanamiento de hitos, sin promesas medicas. No muestres precios ni listes servicios en este primer paso. Cierra preguntando cual categoria o experiencia le interesa. Si el cliente elige una categoria, muestra solo los servicios de esa categoria con nombre canonico, duracion, precio, horario si aplica y descripcion breve del catalogo; cierra preguntando que servicio le gustaria para su bebe. En esta etapa enfocate en elegir servicio: reserva los complementos para la etapa Complementos, despues de guardar service. Cuando el cliente enfoque una opcion exacta del catalogo, registra service usando resolve_service_selection; despues resuelve el tipo de atencion para saber si la ruta es reserva o inscripcion y deja que la regla de la etapa registre fulfillment_ready.",
          "allowedActions": [
            "get_service_catalog",
            "resolve_service_selection",
            "set_fact",
            "get_service_fulfillment"
          ],
          "collect": [
            "service",
            "fulfillment_ready"
          ],
          "entryActions": [
            {
              "tool": "get_service_catalog",
              "arguments": {
                "view": "auto",
                "query": "{{user.message}}"
              }
            },
            {
              "tool": "get_service_fulfillment",
              "arguments": {
                "service": "{{fact.service}}"
              },
              "when": {
                "requiredFacts": [
                  "service"
                ],
                "missingFacts": [
                  "fulfillment_ready"
                ]
              }
            }
          ]
        },
        {
          "id": "addons_offering",
          "name": "Complementos",
          "goal": "Resolver complementos del servicio elegido: ofrecerlos solo si existen compatibles; si no existen, cerrar esta etapa internamente.",
          "afterTool": [
            {
              "tool": "get_compatible_add_ons",
              "when": {
                "path": "data.count",
                "equals": "0"
              },
              "setFact": {
                "key": "add_ons",
                "value": "ninguno"
              }
            },
            {
              "tool": "get_compatible_add_ons",
              "when": {
                "path": "data.count",
                "notEquals": "0"
              },
              "sendMessageSequence": "addons_catalog_image",
              "sendOncePerConversation": true
            }
          ],
          "advanceWhenFacts": [
            "add_ons"
          ],
          "conversationGuidance": "Usa la salida vigente de get_compatible_add_ons como fuente oficial de complementos compatibles. Si el ultimo mensaje ya trae una decision sobre complementos, registrala con set_fact y continua sin volver a preguntar. Si el mensaje pide resumen, link o actualizar anticipo junto con una decision de complementos, registra primero add_ons y no respondas resumen ni link desde esta etapa. Si existen complementos y aun falta decision, presenta opciones oficiales y termina con una pregunta simple: Quieres agregar decoracion, fotografia, ambas opciones o seguimos sin complementos? Si el cliente rechaza o no aplican, registra add_ons=ninguno y continua. El estado conversacional es solicitud en preparacion hasta que checkout o reserva sean devueltos por una herramienta.",
          "allowedActions": [
            "get_compatible_add_ons",
            "resolve_service_selection",
            "set_fact"
          ],
          "collect": [
            "add_ons"
          ],
          "reentryOnFactChanged": [
            "service"
          ],
          "entryActions": [
            {
              "tool": "get_compatible_add_ons",
              "arguments": {
                "service": "{{fact.service}}"
              }
            }
          ]
        },
        {
          "id": "scheduling",
          "name": "Agenda",
          "goal": "Revisar disponibilidad y validar fecha y hora para una reserva por hora.",
          "afterTool": [
            {
              "tool": "check_availability",
              "when": {
                "path": "data.date"
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
          "skipWhen": "fixed_schedule_label",
          "reentryOnFactChanged": [
            "service",
            "desired_date",
            "desired_time"
          ],
          "conversationGuidance": "Valida agenda con servicio canonico. Si falta fecha, pregunta el dia. Si ya hay fecha pero falta hora, llama check_availability con service y date sin time para mostrar los espacios disponibles del dia; no preguntes primero si quiere ver horarios. Si el cliente da una hora exacta, valida con check_availability usando service, date y time. Si el horario no esta disponible, presenta horarios devueltos por disponibilidad y aplica el seleccionado. Disponibilidad con slot_held=false significa horario libre para continuar la solicitud. Si ya existe fixed_schedule_label, esta etapa se salta porque el servicio es de inscripcion.",
          "allowedActions": [
            "check_availability",
            "set_fact"
          ],
          "collect": [
            "desired_date",
            "desired_time"
          ],
          "entryActions": [
            {
              "tool": "check_availability",
              "arguments": {
                "service": "{{fact.service}}",
                "date": "{{fact.desired_date}}",
                "time": "{{fact.desired_time}}"
              },
              "when": {
                "requiredFacts": [
                  "service",
                  "desired_date"
                ],
                "missingFacts": [
                  "availability_checked"
                ]
              }
            }
          ]
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
          "allowedActions": [
            "set_fact"
          ],
          "collect": [
            "customer_name",
            "baby_birth_date"
          ]
        },
        {
          "id": "finalization",
          "name": "Cierre con anticipo",
          "goal": "Preparar el resumen, generar el link de anticipo y esperar confirmacion automatica de pago.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Prepara checkout cuando los facts requeridos esten completos. Antes de pago aprobado o reserva creada por herramienta, habla de solicitud, datos o link pendiente; no digas tu reserva, cambie tu reserva ni reserva confirmada. Si el cliente pide cambiar la hora o fecha sin dar el nuevo valor, pregunta a que hora o fecha actualizas la solicitud. Nunca construyas, copies ni reutilices un resumen o link del historial. Si el cliente pide resumen, link, anticipo o actualizar resumen, usa los facts actuales y llama prepare_checkout. Si el cliente cambia fecha u hora antes del pago, actualiza el fact con set_fact o con el resultado de check_availability, valida disponibilidad con servicio, fecha y hora actuales y despues llama prepare_checkout si ya habia resumen o link pendiente. Si cambia servicio o complementos antes del pago, actualiza primero el fact correspondiente; para quitar complementos registra add_ons=ninguno. Presenta como vigente solo el resumen o link devuelto por la herramienta. Si el checkout no requiere pago, pide confirmacion verbal del resumen y crea la reserva solo cuando customer_confirmed=true venga del ultimo mensaje del cliente.",
          "allowedActions": [
            "prepare_checkout",
            "create_reservation",
            "verify_payment",
            "get_service_catalog",
            "resolve_service_selection",
            "check_availability",
            "get_compatible_add_ons",
            "set_fact",
            "reset_flow_context",
            "send_message_sequence"
          ],
          "collect": [
            "service",
            "add_ons",
            "desired_date",
            "desired_time",
            "customer_name",
            "baby_birth_date",
            "customer_confirmed"
          ],
          "entryActions": [
            {
              "tool": "prepare_checkout",
              "arguments": {},
              "when": {
                "requiredFacts": [
                  "service",
                  "add_ons",
                  "customer_name",
                  "baby_birth_date",
                  "fulfillment_ready"
                ],
                "missingVerifications": [
                  "checkout_prepared"
                ]
              }
            }
          ],
          "afterTool": [
            {
              "tool": "check_availability",
              "when": {
                "path": "data.date"
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
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "reservation_management",
          "name": "Gestion de reserva existente",
          "goal": "Gestionar una reserva existente sin mezclarla con una solicitud nueva.",
          "collect": [
            "desired_date",
            "desired_time"
          ],
          "allowedActions": [
            "get_customer_reservations",
            "manage_reservation",
            "escalate_to_human"
          ],
          "entryActions": [
            {
              "tool": "get_customer_reservations",
              "arguments": {}
            }
          ],
          "conversationGuidance": "Este flow solo aplica a reservas existentes. Si el cliente pide cambiar fecha u hora de una reserva existente, usa manage_reservation con el nuevo dato; la tool valida disponibilidad y aplica el cambio si corresponde. Si pide cambiar servicio o adicionales de una reserva ya confirmada, llama manage_reservation; la tool decide si coloca la reserva en espera y escala. Si hay varias reservas, usa get_customer_reservations o pide que la identifique por fecha, hora o servicio; nunca pidas UUID al cliente. No generes checkout nuevo para cambios de una reserva ya pagada. Si el cliente empieza una solicitud nueva, deja que el router vuelva al flow principal."
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
-- -- Vincular WhatsApp del negocio al agente ----------------------------------
UPDATE dbo.BusinessWhatsAppNumbers
SET AgentId = @AgentId,
    WhatsAppBusinessAccountId = COALESCE(NULLIF(WhatsAppBusinessAccountId, N''), N'2562841327443156')
WHERE BusinessId = @BusinessId
  AND (AgentId IS NULL OR AgentId <> @AgentId OR WhatsAppBusinessAccountId IS NULL OR LTRIM(RTRIM(WhatsAppBusinessAccountId)) = N'');
PRINT N'SeedAgenticConfiguration: Mimi Bot configured for business ' + CAST(@BusinessId AS NVARCHAR(36));
GO
