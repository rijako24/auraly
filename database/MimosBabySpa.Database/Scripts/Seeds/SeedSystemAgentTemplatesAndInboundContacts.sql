-- =============================================================================

-- SeedSystemAgentTemplatesAndInboundContacts.sql

--

-- Templates del sistema y contactos inbound operativos por negocio.

-- Mantiene separados los agentes de domicilio y operations.

-- =============================================================================



SET NOCOUNT ON;



DECLARE @DeliveryTemplateId UNIQUEIDENTIFIER = 'A1111111-1111-1111-1111-111111111111';

DECLARE @OperationsTemplateId UNIQUEIDENTIFIER = 'A2222222-2222-2222-2222-222222222222';



DECLARE @DeliverySettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "historyWindowSize": 12,
  "persona": "Eres el asistente de domicilios del negocio. Atiendes solo a domiciliarios y coordinas si toman o rechazan solicitudes asignadas por WhatsApp.",
  "policies": "Responde breve y operativo. Tu funcion es resolver solicitudes de domicilio pendientes. No atiendas clientes finales ni solicitudes administrativas.",
  "notifications": {},
  "webhooks": {},
  "escalations": {
    "human": {
      "contacts": []
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {}
  },
  "flows": [
    {
      "id": "order_request",
      "type": "primary",
      "routingGuidance": "Use this primary flow for external order request interactions with delivery contacts.",
      "stages": [
        {
          "id": "order_request",
          "name": "Gestion de domicilio",
          "goal": "Resolver si el domiciliario acepta o rechaza una solicitud pendiente.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Si el mensaje viene citado/respondiendo a una solicitud de domicilio, la cita identifica el pedido: si el contacto acepta/confirma/toma el pedido, acepta la solicitud; si rechaza o dice que no puede tomarlo, rechaza la solicitud. No pidas confirmacion ni motivo en esos casos. Busca el pedido solo cuando no haya cita ni payload interactivo, cuando necesites resolver por codigo PED/datos del pedido, o cuando haya varias ordenes pendientes; si hay ambiguedad, pide elegir mostrando request_code. Si el pedido esta vencido o no disponible, responde breve indicando que ya no puede gestionarse automaticamente. Tras aceptar agradece la confirmacion; tras rechazar indica que se registro el rechazo.",
          "collect": [],
          "signals": [
            {
              "type": "order_lookup",
              "description": "Consulta o referencia a una solicitud de domicilio pendiente.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "order_accept",
              "description": "Aceptaci?n clara de la solicitud de domicilio pendiente.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "order_reject",
              "description": "Rechazo claro de la solicitud de domicilio pendiente.",
              "valueSchema": {
                "type": "string"
              }
            }
          ],
          "actions": [
            {
              "id": "search_order",
              "operation": "internal.search_order",
              "trigger": "on_signal",
              "signal": "order_lookup",
              "arguments": {
                "query": "{{signal.order_lookup.value}}"
              },
              "onOutcome": {
                "internal.order_loaded": {}
              }
            },
            {
              "id": "accept_order",
              "operation": "internal.accept_order",
              "trigger": "on_signal",
              "signal": "order_accept",
              "arguments": {
                "response_text": "{{signal.order_accept.value}}"
              },
              "onOutcome": {
                "internal.order_accepted": {}
              }
            },
            {
              "id": "reject_order",
              "operation": "internal.reject_order",
              "trigger": "on_signal",
              "signal": "order_reject",
              "arguments": {
                "response_text": "{{signal.order_reject.value}}"
              },
              "onOutcome": {
                "internal.order_rejected": {}
              }
            }
          ]
        }
      ]
    }
  ],
  "factSchema": []
}';



DECLARE @OperationsSettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "historyWindowSize": 12,
  "persona": "Eres el agente operativo interno del negocio. Atiendes solo contactos administrativos autorizados.",
  "policies": "Responde de forma breve y operativa. No atiendas solicitudes de clientes finales ni de domiciliarios. Todas las consultas y cambios deben basarse en resultados operativos vigentes del negocio actual. Para reagendar reservas por inconvenientes operativos, usa operations_request_reschedule para enviar el aviso al cliente y dejar que su respuesta siga por el flujo normal. No cambies fecha u hora desde operaciones.",
  "notifications": {},
  "webhooks": {},
  "templates": {
    "availability_slots": "{{#if intro_message}}\n{{intro_message}}\n\n{{/if}}*Espacios disponibles para {{date_formatted}}* ({{service_name}})\n\n{{#each options}}\n- {{this}}\n{{/each}}\n\nCual espacio prefieres?"
  },
  "escalations": {
    "human": {
      "contacts": []
    },
    "external": {
      "enabled": false,
      "events": {}
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {}
  },
  "flows": [
    {
      "id": "order_request",
      "type": "primary",
      "routingGuidance": "Use this primary flow for external order request interactions with delivery contacts.",
      "stages": [
        {
          "id": "operations",
          "name": "Operacion interna",
          "goal": "Atender mensajes operativos autorizados del negocio: agenda, bloqueos, metricas, pedidos, ventas e historial de clientes.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Consulta reservas operativas para preguntas de agenda por dia o rango. Bloquea disponibilidad para bloquear horarios o dias. Consulta metricas de negocio para ventas, pedidos, reservas y servicios mas vendidos. Consulta historial de cliente para ultima compra o historial de un cliente. Solicita reagenda operativa para avisar a clientes afectados que deben reagendar; no muevas reservas directamente desde operaciones.",
          "collect": [],
          "signals": [
            {
              "type": "reservations_query",
              "description": "reservations query",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "date": {
                    "type": "string"
                  },
                  "end_date": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "status": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "customer": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "limit": {
                    "anyOf": [
                      {
                        "type": "integer"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  }
                },
                "required": [
                  "date",
                  "end_date",
                  "status",
                  "customer",
                  "limit"
                ]
              }
            },
            {
              "type": "availability_block",
              "description": "availability block",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "date": {
                    "type": "string"
                  },
                  "end_date": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "start_time": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "end_time": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "employee_id": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "employee_name": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "reason": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "preview_only": {
                    "type": "boolean"
                  }
                },
                "required": [
                  "date",
                  "end_date",
                  "start_time",
                  "end_time",
                  "employee_id",
                  "employee_name",
                  "reason",
                  "preview_only"
                ]
              }
            },
            {
              "type": "reschedule_request",
              "description": "reschedule request",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "date": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "end_date": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "reservation_ids": {
                    "anyOf": [
                      {
                        "type": "array",
                        "items": {
                          "type": "string"
                        }
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "reason": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "preview_only": {
                    "type": "boolean"
                  }
                },
                "required": [
                  "date",
                  "end_date",
                  "reservation_ids",
                  "reason",
                  "preview_only"
                ]
              }
            },
            {
              "type": "metrics_query",
              "description": "metrics query",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "date": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "end_date": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  }
                },
                "required": [
                  "date",
                  "end_date"
                ]
              }
            },
            {
              "type": "customer_history_query",
              "description": "customer history query",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "customer_phone": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "customer": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  },
                  "limit": {
                    "anyOf": [
                      {
                        "type": "integer"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  }
                },
                "required": [
                  "customer_phone",
                  "customer",
                  "limit"
                ]
              }
            },
            {
              "type": "availability_query",
              "description": "availability query",
              "valueSchema": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "service": {
                    "type": "string"
                  },
                  "date": {
                    "type": "string"
                  },
                  "time": {
                    "anyOf": [
                      {
                        "type": "string"
                      },
                      {
                        "type": "null"
                      }
                    ]
                  }
                },
                "required": [
                  "service",
                  "date",
                  "time"
                ]
              }
            }
          ],
          "actions": [
            {
              "id": "get_reservations",
              "operation": "internal.get_reservations",
              "trigger": "on_signal",
              "signal": "reservations_query",
              "arguments": {
                "date": "{{signal.reservations_query.value.date}}",
                "end_date": "{{signal.reservations_query.value.end_date}}",
                "status": "{{signal.reservations_query.value.status}}",
                "customer": "{{signal.reservations_query.value.customer}}",
                "limit": "{{signal.reservations_query.value.limit}}"
              },
              "onOutcome": {
                "internal.reservations_loaded": {}
              }
            },
            {
              "id": "block_availability",
              "operation": "internal.block_availability",
              "trigger": "on_signal",
              "signal": "availability_block",
              "arguments": {
                "date": "{{signal.availability_block.value.date}}",
                "end_date": "{{signal.availability_block.value.end_date}}",
                "start_time": "{{signal.availability_block.value.start_time}}",
                "end_time": "{{signal.availability_block.value.end_time}}",
                "employee_id": "{{signal.availability_block.value.employee_id}}",
                "employee_name": "{{signal.availability_block.value.employee_name}}",
                "reason": "{{signal.availability_block.value.reason}}",
                "preview_only": "{{signal.availability_block.value.preview_only}}"
              },
              "onOutcome": {
                "internal.availability_blocked": {}
              }
            },
            {
              "id": "request_reschedule",
              "operation": "internal.request_reschedule",
              "trigger": "on_signal",
              "signal": "reschedule_request",
              "arguments": {
                "date": "{{signal.reschedule_request.value.date}}",
                "end_date": "{{signal.reschedule_request.value.end_date}}",
                "reservation_ids": "{{signal.reschedule_request.value.reservation_ids}}",
                "reason": "{{signal.reschedule_request.value.reason}}",
                "preview_only": "{{signal.reschedule_request.value.preview_only}}"
              },
              "onOutcome": {
                "internal.reschedule_requested": {}
              }
            },
            {
              "id": "get_metrics",
              "operation": "internal.get_business_metrics",
              "trigger": "on_signal",
              "signal": "metrics_query",
              "arguments": {
                "date": "{{signal.metrics_query.value.date}}",
                "end_date": "{{signal.metrics_query.value.end_date}}"
              },
              "onOutcome": {
                "internal.metrics_loaded": {}
              }
            },
            {
              "id": "get_customer_history",
              "operation": "internal.get_customer_history",
              "trigger": "on_signal",
              "signal": "customer_history_query",
              "arguments": {
                "customer_phone": "{{signal.customer_history_query.value.customer_phone}}",
                "customer": "{{signal.customer_history_query.value.customer}}",
                "limit": "{{signal.customer_history_query.value.limit}}"
              },
              "onOutcome": {
                "internal.customer_history_loaded": {}
              }
            },
            {
              "id": "check_availability",
              "operation": "reservation.check_availability",
              "trigger": "on_signal",
              "signal": "availability_query",
              "arguments": {
                "service": "{{signal.availability_query.value.service}}",
                "date": "{{signal.availability_query.value.date}}",
                "time": "{{signal.availability_query.value.time}}"
              },
              "onOutcome": {
                "availability.exact_time_available": {},
                "availability.options_available": {},
                "availability.requested_time_unavailable": {},
                "availability.none": {}
              }
            }
          ]
        }
      ]
    }
  ],
  "factSchema": []
}';



IF ISJSON(@DeliverySettingsJson) <> 1

    THROW 51000, 'SeedSystemAgentTemplatesAndInboundContacts: Delivery SettingsJson invalido.', 1;



IF ISJSON(@OperationsSettingsJson) <> 1

    THROW 51000, 'SeedSystemAgentTemplatesAndInboundContacts: Operations SettingsJson invalido.', 1;



MERGE dbo.AgentTemplates AS target

USING (VALUES

    (@DeliveryTemplateId, N'system.domicilio', N'Agente de domicilios', N'domicilio', N'Resuelve interacciones externas con domiciliarios.', @DeliverySettingsJson),

    (@OperationsTemplateId, N'system.operations', N'Agente operativo', N'operations', N'Atiende contactos administrativos y operativos del negocio.', @OperationsSettingsJson)

) AS source (AgentTemplateId, [Key], [Name], Kind, [Description], SettingsJson)

ON target.AgentTemplateId = source.AgentTemplateId

   OR target.[Key] = source.[Key]

WHEN MATCHED THEN

    UPDATE SET [Key] = source.[Key],

               [Name] = source.[Name],

               Kind = source.Kind,

               [Description] = source.[Description],

               SettingsJson = source.SettingsJson,

               IsSystemTemplate = 1,

               IsActive = 1,

               UpdatedAt = GETUTCDATE()

WHEN NOT MATCHED THEN

    INSERT (AgentTemplateId, [Key], [Name], Kind, [Description], SettingsJson, IsSystemTemplate, IsActive, CreatedAt)

    VALUES (source.AgentTemplateId, source.[Key], source.[Name], source.Kind, source.[Description], source.SettingsJson, 1, 1, GETUTCDATE());



DECLARE @AgentTypeId UNIQUEIDENTIFIER;

SELECT TOP (1) @AgentTypeId = AgentTypeId

FROM dbo.AgentTypes

WHERE IsActive = 1

ORDER BY Name;



IF @AgentTypeId IS NULL

BEGIN

    PRINT N'SeedSystemAgentTemplatesAndInboundContacts: AgentType activo no encontrado; omitiendo agentes inbound.';

    RETURN;

END



DECLARE @SolorzanoBusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @SolorzanoDeliveryAgentId UNIQUEIDENTIFIER = 'D0EE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @SolorzanoOperationsAgentId UNIQUEIDENTIFIER = 'D1EE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

DECLARE @MimosOperationsAgentId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-2222222220A1';

DECLARE @LuisBusinessId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-000000000001';

DECLARE @LuisOperationsAgentId UNIQUEIDENTIFIER = 'BABA0000-0000-0000-0000-0000000000A1';



IF EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @SolorzanoDeliveryAgentId)

BEGIN

    UPDATE dbo.Agents

    SET Kind = N'domicilio',

        AgentTemplateId = @DeliveryTemplateId,

        SettingsJson = @DeliverySettingsJson,

        Model = N'gpt-4.1-mini',

        Temperature = 0.2,

        UpdatedAt = GETUTCDATE()

    WHERE AgentId = @SolorzanoDeliveryAgentId;

END



DECLARE @OpsAgents TABLE (BusinessId UNIQUEIDENTIFIER, AgentId UNIQUEIDENTIFIER, [Name] NVARCHAR(200), [Description] NVARCHAR(500));

INSERT INTO @OpsAgents (BusinessId, AgentId, [Name], [Description]) VALUES

    (@SolorzanoBusinessId, @SolorzanoOperationsAgentId, N'Operaciones Solorzano', N'Agente operativo interno de Vinos Artesanales Solorzano.'),

    (@MimosBusinessId, @MimosOperationsAgentId, N'Operaciones Mimos', N'Agente operativo interno de Mimos Baby Spa.'),

    (@LuisBusinessId, @LuisOperationsAgentId, N'Operaciones Luis Petit', N'Agente operativo interno de Luis Petit Barber.');



MERGE dbo.Agents AS target

USING (

    SELECT BusinessId, AgentId, [Name], [Description]

    FROM @OpsAgents oa

    WHERE EXISTS (SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId = oa.BusinessId)

) AS source

ON target.AgentId = source.AgentId

WHEN MATCHED THEN

    UPDATE SET BusinessId = source.BusinessId,

               AgentTypeId = @AgentTypeId,

               AgentTemplateId = @OperationsTemplateId,

               [Name] = source.[Name],

               [Description] = source.[Description],

               Kind = N'operations',

               IsActive = 1,

               SettingsJson = @OperationsSettingsJson,

               Model = N'gpt-4.1-mini',

               Temperature = 0.2,

               UpdatedAt = GETUTCDATE()

WHEN NOT MATCHED THEN

    INSERT (AgentId, BusinessId, AgentTypeId, AgentTemplateId, [Name], [Description], Kind, IsActive,

            SettingsJson, Model, Temperature, CreatedAt)

    VALUES (source.AgentId, source.BusinessId, @AgentTypeId, @OperationsTemplateId, source.[Name], source.[Description], N'operations', 1,

            @OperationsSettingsJson, N'gpt-4.1-mini', 0.2, GETUTCDATE());



DECLARE @Contacts TABLE (

    BusinessInboundContactId UNIQUEIDENTIFIER,

    BusinessId UNIQUEIDENTIFIER,

    [Type] NVARCHAR(50),

    [Key] NVARCHAR(100),

    [Name] NVARCHAR(200),

    [Role] NVARCHAR(100),

    PhoneNumber NVARCHAR(50),

    PhoneNormalized NVARCHAR(50),

    InboundAgentId UNIQUEIDENTIFIER,

    CapabilitiesJson NVARCHAR(MAX)

);



INSERT INTO @Contacts VALUES

    ('E2EE3BA9-E6BF-43E2-8C1A-560CB724688B', @SolorzanoBusinessId, N'domicilio', N'supervoy', N'SuperVoy', N'domicilio', N'+573023823535', N'573023823535', @SolorzanoDeliveryAgentId, N'{"scope":"domicilio"}'),

    ('E1EE3BA9-E6BF-43E2-8C1A-560CB724688B', @SolorzanoBusinessId, N'operations', N'operaciones_solorzano', N'Operaciones Solorzano', N'operations', N'+573004442469', N'573004442469', @SolorzanoOperationsAgentId, N'{"scope":"operations"}'),

    ('22222222-2222-2222-2222-2222222220B1', @MimosBusinessId, N'operations', N'operaciones_mimos', N'Operaciones Mimos', N'operations', N'+573012926660', N'573012926660', @MimosOperationsAgentId, N'{"scope":"operations"}'),

    ('BABA0000-0000-0000-0000-0000000000B1', @LuisBusinessId, N'operations', N'operaciones_luis_petit', N'Operaciones Luis Petit', N'operations', N'+573042052007', N'573042052007', @LuisOperationsAgentId, N'{"scope":"operations"}');



MERGE dbo.BusinessInboundContacts AS target

USING (

    SELECT *

    FROM @Contacts c

    WHERE EXISTS (SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId = c.BusinessId)

      AND EXISTS (SELECT 1 FROM dbo.Agents a WHERE a.AgentId = c.InboundAgentId AND a.BusinessId = c.BusinessId)

) AS source

ON target.BusinessId = source.BusinessId AND target.PhoneNormalized = source.PhoneNormalized

WHEN MATCHED THEN

    UPDATE SET BusinessInboundContactId = source.BusinessInboundContactId,

               [Type] = source.[Type],

               [Key] = source.[Key],

               [Name] = source.[Name],

               [Role] = source.[Role],

               PhoneNumber = source.PhoneNumber,

               InboundAgentId = source.InboundAgentId,

               CapabilitiesJson = source.CapabilitiesJson,

               IsActive = 1,

               UpdatedAt = GETUTCDATE()

WHEN NOT MATCHED THEN

    INSERT (BusinessInboundContactId, BusinessId, [Type], [Key], [Name], [Role], PhoneNumber, PhoneNormalized,

            InboundAgentId, CapabilitiesJson, IsActive, CreatedAt)

    VALUES (source.BusinessInboundContactId, source.BusinessId, source.[Type], source.[Key], source.[Name], source.[Role], source.PhoneNumber, source.PhoneNormalized,

            source.InboundAgentId, source.CapabilitiesJson, 1, GETUTCDATE());



DELETE FROM dbo.BusinessInboundContacts

WHERE BusinessId = @SolorzanoBusinessId

  AND BusinessInboundContactId = 'E0EE3BA9-E6BF-43E2-8C1A-560CB724688B'

  AND [Type] = N'domicilio'

  AND PhoneNormalized = N'573042052007';



DELETE FROM dbo.BusinessInboundContacts

WHERE BusinessId = @SolorzanoBusinessId

  AND BusinessInboundContactId = 'E3EE3BA9-E6BF-43E2-8C1A-560CB724688B'

  AND [Type] = N'domicilio'

  AND PhoneNormalized = N'573006704013';



PRINT N'SeedSystemAgentTemplatesAndInboundContacts: templates y contactos inbound configurados.';
