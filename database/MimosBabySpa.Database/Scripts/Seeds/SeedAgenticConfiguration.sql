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

  "flow": {

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

        "conversationGuidance": "Si el primer mensaje es solo saludo, agrega solo una pregunta simple de ayuda: En que puedo ayudarte hoy con el bienestar de tu bebe? Si el primer mensaje tambien trae informacion o una intencion, no agregues una pregunta generica; continua con esa informacion y con la etapa que corresponda. Cuando el cliente comparta nombre o edad del bebe, capturalos. Cuando la intencion sea reserva nueva o recomendacion personalizada, pide solo el dato faltante necesario para avanzar.",

        "allowedActions": [

          "resolve_service_selection",

          "set_fact"

        ],

        "collect": [

          "baby_name",

          "baby_age_months"

        ],

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

        "conversationGuidance": "Al entrar en esta etapa, consulta el catalogo oficial antes de presentar categorias, servicios, precios u horarios. Con el catalogo retornado, presenta primero solo las categorias reales con una descripcion breve de la experiencia de cada una y sus beneficios generales para el bebe segun edad/etapa si esta disponible; no muestres precios ni listes servicios en este primer paso. Cierra preguntando cual categoria o experiencia le interesa. Si el cliente elige una categoria, muestra solo los servicios de esa categoria con nombre canonico, duracion, precio, horario si aplica y descripcion breve del catalogo; cierra preguntando que servicio le gustaria para su bebe. En esta etapa enfocate en elegir servicio: reserva los complementos para la etapa Complementos, despues de guardar service. Cuando el cliente enfoque una opcion exacta del catalogo, registra service usando el nombre canonico; despues resuelve el tipo de atencion para saber si la ruta es reserva o inscripcion y deja que la regla de la etapa registre fulfillment_ready.",

        "allowedActions": [

          "consultar_catalogo",

          "resolve_service_selection",

          "set_fact",

          "resolver_tipo_atencion"

        ],

        "collect": [

          "service",

          "fulfillment_ready"

        ],

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

        "conversationGuidance": "Consulta complementos compatibles con el servicio exacto seleccionado. Antes de ofrecer complementos, confirma la eleccion del servicio en tono calido y agrega una descripcion breve con beneficios sintetizados desde la informacion oficial del catalogo y la etapa del bebe si esta disponible. Cuando existan complementos compatibles, usa la lista retornada como fuente de nombres canonicos. Presenta las familias disponibles de forma natural: decoraciones y fotografias, con una descripcion breve de cada familia. Para decoracion, explica que permite ambientar la experiencia con detalles tematicos o personalizados. Para fotografia, explica que permite guardar el recuerdo en fotos digitales, impresas o video segun la opcion elegida; presenta las condiciones de disponibilidad como nota informativa del complemento. Presenta solo las familias y sus descripciones breves; los nombres y detalles de cada opcion van en las imagenes adjuntas. Menciona que los detalles estan en las imagenes adjuntas y pregunta si desea agregar decoracion, fotografia, ambas opciones o continuar sin complementos. Haz una sola pregunta final sobre complementos. El fact add_ons se completa con add_ons=ninguno o con nombres canonicos compatibles. Si el cliente continua sin complementos, registra add_ons=ninguno. Si el cliente expresa interes por una familia o grupo de complementos y esa seleccion puede corresponder a varias opciones compatibles, mantente en complementos: usa los complementos compatibles vigentes o refrescalos, y pide que elija una opcion especifica por nombre o desde la imagen. Cuando tengas un nombre canonico compatible o una autorizacion explicita para que Mimi elija, registra add_ons. Si el complemento registrado no se incluye en el total de checkout, informa que su disponibilidad se validara con el proveedor correspondiente y que su valor es informativo, sin incluirse en el anticipo. El cliente puede elegir complementos de grupos distintos; si la herramienta indica grupo duplicado, pide que conserve una sola opcion de ese grupo. Si pide ambas categorias, registra un nombre canonico por cada grupo elegido, separados por coma. Si la herramienta indica complementos ambiguos, pide al cliente que elija una opcion especifica de los complementos compatibles. Despues de registrar add_ons, continua con el siguiente paso natural del flujo. Cuando no existan complementos compatibles, registra add_ons=ninguno y deja que el flujo avance.",

        "allowedActions": [

          "consultar_complementos",

          "set_fact"

        ],

        "collect": [

          "add_ons"

        ],

      },

      {

        "id": "scheduling",

        "name": "Agenda",

        "goal": "Guiar al cliente hacia el siguiente paso de agenda o inscripcion segun la ruta oficial del servicio elegido.",

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

        "skipWhen": "fixed_schedule_label",

        "reentryOnFactChanged": [

          "service",

          "desired_date",

          "desired_time",

          "fixed_schedule_label",

          "fulfillment_ready"

        ],

        "conversationGuidance": "Usa esta etapa solo para reservas con fulfillment_ready=reservation. Si falta fecha, pidela; si el cliente pide horarios para una fecha, valida disponibilidad con esa fecha y muestra horarios oficiales; si el cliente elige una hora de horarios recien presentados, registra desired_date y desired_time y valida disponibilidad con fecha y hora en el mismo turno. Despues de confirmar disponibilidad, informa el resultado y continua con el siguiente dato del flujo. Si ya existe fixed_schedule_label, esta etapa se salta porque el servicio es de inscripcion.",

        "allowedActions": [

          "check_availability",

          "set_fact"

        ],

        "collect": [

          "availability_checked"

        ],

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

        ],

      },

      {

        "id": "finalization",

        "name": "Cierre",

        "goal": "Cierra la reserva: resumen, pago o confirmacion verbal, registro de cita y mensajes post-reserva.",

        "advanceWhenFacts": [],

        "conversationGuidance": "Objetivo: cerrar solo la solicitud actual con resumen, pago o confirmacion segun checkout. Si aun no se mostro el resumen y ya estan los datos requeridos, prepara el checkout con el servicio exacto del catalogo; la herramienta resuelve precio, plantilla, monto y link. Si hay link/resumen pendiente y el cliente solo pide informacion normal, responde sin cambiar la solicitud. Si hay link/resumen pendiente y el cliente pide agregar o cambiar complementos sin nombrar uno exacto, consulta complementos compatibles y pide cual desea; si cambia servicio o complemento exacto, actualiza los facts correspondientes y reconstruye el resumen/link. Premisa de avance: cuando el cliente elige una opcion concreta de una lista recien presentada, esa eleccion autoriza el siguiente paso; registra el nombre exacto de esa opcion como service, prepara checkout y entrega el resumen/link resultante. Si el cliente pide una categoria o servicio no exacto, consulta el catalogo oficial y ofrece opciones exactas; cuando elija una, aplica la premisa de avance. Si quiere empezar otra solicitud distinta, pregunta si reemplaza la actual o la deja sin efecto; si decide desistir, reinicia el flujo con reason=start_new_request o customer_abandoned y checkout_action=abandon. Si checkout entrega enlace de pago, comparte el resumen/link y espera la confirmacion automatica del webhook. Si checkout entrega un cierre sin pago, pregunta si confirma con esa informacion; cuando confirme verbalmente, crea la reserva. Para servicios con horario oficial de inscripcion, prepara el checkout con ese horario y espera la confirmacion automatica del webhook.",

        "allowedActions": [

          "preparar_checkout",

          "crear_reserva",

          "verify_payment",

          "consultar_catalogo",

          "consultar_complementos",

          "set_fact",

          "reset_flow_context",

          "send_message_sequence"

        ],

        "collect": [],

      }

    ],

    "language": {

      "actions": {

        "set_fact": {

          "name": "Registrar dato",

          "purpose": "Guardar datos expresados por el cliente cuando son necesarios para avanzar.",

          "tool": "set_fact"

        },

        "consultar_catalogo": {

          "name": "Consultar catalogo oficial",

          "purpose": "Presentar categorias o servicios oficiales segun la intencion del cliente.",

          "tool": "get_service_catalog"

        },

        "resolve_service_selection": {

          "name": "Resolver servicio exacto",

          "purpose": "Convertir la seleccion del cliente en un servicio canonico del catalogo.",

          "tool": "resolve_service_selection"

        },

        "resolver_tipo_atencion": {

          "name": "Resolver tipo de atencion",

          "purpose": "Determinar si el servicio requiere agenda, inscripcion u otra ruta de cumplimiento.",

          "tool": "get_service_fulfillment"

        },

        "consultar_complementos": {

          "name": "Consultar complementos compatibles",

          "purpose": "Obtener complementos oficiales compatibles con la seleccion actual.",

          "tool": "get_compatible_add_ons"

        },

        "check_availability": {

          "name": "Validar disponibilidad",

          "purpose": "Consultar agenda oficial y confirmar fecha y hora disponibles.",

          "tool": "check_availability"

        },

        "preparar_checkout": {

          "name": "Preparar resumen y pago",

          "purpose": "Generar resumen oficial, total y link o confirmacion segun configuracion.",

          "tool": "prepare_checkout"

        },

        "crear_reserva": {

          "name": "Crear reserva",

          "purpose": "Crear la reserva cuando los datos requeridos y verificaciones esten completos.",

          "tool": "create_reservation"

        },

        "verify_payment": {

          "name": "Verificar pago",

          "purpose": "Consultar el estado de pago vigente.",

          "tool": "verify_payment"

        },

        "reset_flow_context": {

          "name": "Reiniciar solicitud",

          "purpose": "Limpiar el contexto de la solicitud actual segun la intencion del cliente.",

          "tool": "reset_flow_context"

        },

        "send_message_sequence": {

          "name": "Enviar secuencia configurada",

          "purpose": "Enviar una secuencia declarada en la configuracion del agente.",

          "tool": "send_message_sequence"

        },

        "escalate_to_human": {

          "name": "Escalar a humano",

          "purpose": "Pasar la conversacion a una persona con el contexto necesario.",

          "tool": "escalate_to_human"

        },

        "get_customer_reservations": {

          "name": "Mostrar reservas del cliente",

          "purpose": "Listar reservas gestionables cuando el cliente tiene varias o la solicitud es ambigua.",

          "tool": "get_customer_reservations"

        },

        "manage_reservation": {

          "name": "Gestionar reserva existente",

          "purpose": "Aplicar cambios permitidos, confirmar asistencia, cancelar o escalar cambios no automaticos segun politica configurada.",

          "tool": "manage_reservation"

        },

        "completar_reagenda_pagada": {

          "name": "Completar reagenda pagada",

          "purpose": "Crear la reserva de un pago confirmado pendiente usando la nueva fecha y hora disponible.",

          "tool": "manage_reservation"

        }

      },

      "enabled": true

    }

  },

  "globalActions": [

    {

      "id": "human_escalation",

      "priority": 1000,

      "goal": "Escalar a una persona cuando el cliente lo pida, este frustrado, haya errores consecutivos o la solicitud salga del alcance del bot.",

      "conversationGuidance": "Escala con una razon breve y el ultimo mensaje relevante del cliente.",

      "allowedActions": [

        "escalate_to_human"

      ]

    },

    {

      "id": "complete_paid_reservation_reschedule",

      "priority": 950,

      "goal": "Completar la agenda cuando un pago confirmado quedo sin reserva porque el horario original ya no estaba disponible.",

      "conversationGuidance": "Usa esta ruta solo cuando el estado indique pago confirmado sin reserva enlazada. No generes nuevo checkout ni pidas nuevo pago. Fecha y hora se resuelven por disponibilidad; si el horario no esta disponible, ofrece horarios oficiales devueltos por la herramienta.",

      "allowedActions": [

        "check_availability",

        "completar_reagenda_pagada",

        "set_fact"

      ]

    },

    {

      "id": "manage_existing_reservation",

      "priority": 900,

      "goal": "Gestionar reservas existentes cuando el cliente quiera confirmar asistencia, cambiar, reagendar, agregar o quitar complementos, cambiar servicio o suspender una reserva ya creada.",

      "conversationGuidance": "Gestiona esta ruta antes del flujo de reserva nueva. Primero identifica la reserva cuando haga falta. Fecha y hora pueden aplicarse directamente si hay disponibilidad; cambios de servicio, complementos u otros campos quedan en espera y escalan segun resultado de la herramienta. Si hay varias reservas, pregunta cual por fecha y servicio; nunca pidas UUID al cliente. No generes nuevo checkout para cambios de una reserva ya pagada.",

      "allowedActions": [

        "get_customer_reservations",

        "manage_reservation"

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

      "key": "baby_name",

      "role": "baby.name",

      "label": "nombre del bebe",

      "type": "string",

      "required": true,

      "source": "user",

      "captureMode": "eager",

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

      "captureMode": "eager",

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

      "captureMode": "onDemand",

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

      "aliases": [

        "plan",

        "servicio"

      ]

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

      "aliases": [

        "complemento",

        "decoracion",

        "decoracion",

        "adicional"

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

      "retentionDays": 7,

      "aliases": [

        "fecha"

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

      "retentionDays": 7,

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

        "mi nombre",

        "nombre cliente"

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

        "telefono",

        "celular",

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

    }

  ],

  "guards": {

    "capability:reservation.create": {

      "requires": [

        "verification:availability_checked",

        "verification:customer_identified",

        "verification:checkout_no_payment_prepared",

        "state:no_pending_checkout"

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

  }

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

