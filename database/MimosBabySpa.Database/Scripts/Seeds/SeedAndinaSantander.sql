-- =============================================================================
-- SeedAndinaSantander.sql
--
-- Negocio DISTRIBUCIONES ANDINA SANTANDER con flujo de pedidos abierto, perfil comercial,
-- recomendaciones controladas por catalogo y cierre de pedido.
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @TenantId UNIQUEIDENTIFIER = 'A7D1AA00-0000-0000-0000-000000000001';
DECLARE @BusinessId UNIQUEIDENTIFIER = 'A7D1AA00-0000-0000-0000-000000000010';
DECLARE @AgentId UNIQUEIDENTIFIER = 'A7D1AA00-0000-0000-0000-000000000020';
DECLARE @XionCommerceConnectionId UNIQUEIDENTIFIER = 'A7D1AA00-0000-0000-0000-000000000030';
DECLARE @AgentTypeId UNIQUEIDENTIFIER;
DECLARE @SubscriptionId UNIQUEIDENTIFIER = 'A7D1AA00-0000-0000-0000-000000000040';
DECLARE @PlanId UNIQUEIDENTIFIER;

SELECT TOP (1) @AgentTypeId = AgentTypeId
FROM dbo.AgentTypes
WHERE IsActive = 1
ORDER BY Name;

IF @AgentTypeId IS NULL
BEGIN
    PRINT N'SeedAndinaSantander: AgentType activo no encontrado; omitiendo.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.Tenants (TenantId, [Name], Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'DISTRIBUCIONES ANDINA SANTANDER', N'admin@andinasantander.com', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Tenants
    SET [Name] = N'DISTRIBUCIONES ANDINA SANTANDER',
        Email = N'admin@andinasantander.com',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE TenantId = @TenantId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    INSERT INTO dbo.Businesses
        (BusinessId, TenantId, [Name], [Description], [Address], Phone, Email, Website, TimeZone, IsActive, CreatedAt)
    VALUES
        (@BusinessId, @TenantId, N'DISTRIBUCIONES ANDINA SANTANDER',
         N'Distribuidora de alimentos y productos de consumo para hogares, tiendas y distribuidores.',
         N'Bucaramanga, Santander', N'+573000000000', N'admin@andinasantander.com', N'', N'America/Bogota', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Businesses
    SET TenantId = @TenantId,
        [Name] = N'DISTRIBUCIONES ANDINA SANTANDER',
        [Description] = N'Distribuidora de alimentos y productos de consumo para hogares, tiendas y distribuidores.',
        [Address] = COALESCE(NULLIF([Address], N''), N'Bucaramanga, Santander'),
        Phone = COALESCE(NULLIF(Phone, N''), N'+573000000000'),
        Email = N'admin@andinasantander.com',
        Website = COALESCE(Website, N''),
        TimeZone = N'America/Bogota',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END

MERGE dbo.IntegrationConnections AS target
USING (
    SELECT
        @XionCommerceConnectionId AS IntegrationConnectionId,
        @BusinessId AS BusinessId,
        CAST(1 AS INT) AS ConnectionType,
        CAST(4 AS INT) AS Provider,
        CAST(0 AS INT) AS Capability,
        N'Xion - Andina Santander' AS [Name],
        N'Xion' AS AccountIdentifier,
        N'{"baseUrl":"http://api.andinasantander.com:9091/","currency":"COP","requestTimeoutSeconds":120,"sucursalId":1,"vendedorId":1,"equipoId":1,"bodegaId":1,"empresaId":1,"centroDeCostoId":1,"usuarioId":1,"rutaId":0,"validateStockOnCreate":true,"orderHistoryDays":365,"catalogDiscoveryMaxQueries":512,"catalogDiscoveryConcurrency":8,"catalogProductIdRanges":[{"start":1,"end":20000}],"endpoints":{"customerSync":"WebApi/Vendedores/Sync/Clientes/{vendedorId}/{sucursalId}","productSearch":"WebApi/Vendedores/Consulta/ProductosABuscar/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}/{clienteId}","productSearchWithoutCustomer":"WebApi/Vendedores/Consulta/ProductosABuscarSinCliente/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}","productDetail":"WebApi/Vendedores/Consulta/InfoProducto/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}/{clienteId}","productDetailWithoutCustomer":"WebApi/Vendedores/Consulta/InfoProductoSinCliente/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}","nextOrderNumber":"WebApi/Vendedores/Consulta/Pedido/SiguienteConsecutivo/{equipoId}","createOrder":"WebApi/Vendedores/Nuevo/Pedido/{validarExistencia}","orderHistory":"WebApi/Vendedores/Consulta/Pedidos/{vendedorId}/{fechaInicial}/{fechaFin}/{clienteId}/{rutaId}/{criterio}","verifyOrder":"WebApi/Vendedores/Consulta/VerificarPedido/{pedidoId}"}}' AS SettingsJson,
        CAST(NULL AS NVARCHAR(MAX)) AS SecretsJson,
        CAST(1 AS BIT) AS IsEnabled
) AS source
   ON target.IntegrationConnectionId = source.IntegrationConnectionId
   OR (target.BusinessId = source.BusinessId
       AND target.ConnectionType = source.ConnectionType
       AND target.Provider = source.Provider
       AND target.Capability = source.Capability)
WHEN MATCHED THEN
    UPDATE SET
        ConnectionType = source.ConnectionType,
        Provider = source.Provider,
        Capability = source.Capability,
        [Name] = source.[Name],
        AccountIdentifier = source.AccountIdentifier,
        SettingsJson = source.SettingsJson,
        SecretsJson = COALESCE(target.SecretsJson, source.SecretsJson),
        IsEnabled = source.IsEnabled,
        LastError = NULL,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],
            AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
    VALUES (source.IntegrationConnectionId, source.BusinessId, source.ConnectionType, source.Provider, source.Capability,
            source.[Name], source.AccountIdentifier, source.SettingsJson, source.SecretsJson, source.IsEnabled, GETUTCDATE());

DECLARE @Hours TABLE (DayOfWeek INT NOT NULL, OpenTime TIME(0) NOT NULL, CloseTime TIME(0) NOT NULL);
INSERT INTO @Hours (DayOfWeek, OpenTime, CloseTime)
VALUES
(0, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '18:00')),
(1, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(2, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(3, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(4, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(5, CONVERT(TIME(0), '07:30'), CONVERT(TIME(0), '19:00')),
(6, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '18:00'));

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

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.4,
  "historyWindowSize": 24,
  "commerce": {
    "enabled": true,
    "provider": "Xion",
    "conversation": {
      "contextualConfirmationPhrases": [
        "si",
        "si esa",
        "si es esa",
        "si ese",
        "si es ese",
        "si esta",
        "si es esta",
        "si este",
        "si es este",
        "si correcto",
        "si correcta",
        "confirmo",
        "correcto",
        "correcta",
        "esa",
        "ese",
        "esta",
        "este",
        "esa misma",
        "ese mismo",
        "la primera",
        "el primero"
      ],
      "cartReviewRules": [
        {
          "phrase": "ver carrito",
          "match": "contains"
        },
        {
          "phrase": "mostrar carrito",
          "match": "contains"
        },
        {
          "phrase": "muestrame el carrito",
          "match": "contains"
        },
        {
          "phrase": "como queda el carrito",
          "match": "contains"
        },
        {
          "phrase": "como va el carrito",
          "match": "contains"
        }
      ],
      "productReplacementRules": [
        {
          "phrase": "no es el producto",
          "match": "contains"
        },
        {
          "phrase": "no era ese",
          "match": "contains"
        },
        {
          "phrase": "no lo quiero",
          "match": "contains"
        },
        {
          "phrase": "no quiero ese",
          "match": "contains"
        },
        {
          "phrase": "no me sirve",
          "match": "contains"
        },
        {
          "phrase": "quiero cambiar",
          "match": "contains"
        },
        {
          "phrase": "cambialo por",
          "match": "contains"
        },
        {
          "phrase": "reemplazalo por",
          "match": "contains"
        }
      ],
      "candidateSelectionPhrases": [
        "esta",
        "esa",
        "primera",
        "primero",
        "segunda",
        "segundo",
        "tercera",
        "tercero",
        "ultima",
        "ultimo"
      ],
      "clauseSeparators": [
        "y",
        "e",
        "tambien",
        "ademas"
      ],
      "additionalRequestPhrases": [
        "otra",
        "otro",
        "adicional",
        "adicionales",
        "mas",
        "nuevamente",
        "tambien agrega",
        "tambien agregame",
        "tambien anade"
      ],
      "quantityWords": {
        "un": 1,
        "una": 1,
        "uno": 1,
        "dos": 2,
        "tres": 3,
        "cuatro": 4,
        "cinco": 5,
        "seis": 6,
        "siete": 7,
        "ocho": 8,
        "nueve": 9,
        "diez": 10,
        "once": 11,
        "doce": 12,
        "trece": 13,
        "catorce": 14,
        "quince": 15,
        "dieciseis": 16,
        "diecisiete": 17,
        "dieciocho": 18,
        "diecinueve": 19,
        "veinte": 20
      }
    },
    "pendingCart": {
      "discardOnFinalizeIssueCodes": [
        "product_unavailable",
        "product_not_found"
      ],
      "finalizeConfirmationPhrases": [
        "si",
        "correcto",
        "confirmo"
      ],
      "cancellationRules": [
        {
          "phrase": "sin ese",
          "match": "contains"
        },
        {
          "phrase": "sin esa",
          "match": "contains"
        },
        {
          "phrase": "sin eso",
          "match": "contains"
        },
        {
          "phrase": "dejalo por fuera",
          "match": "contains"
        },
        {
          "phrase": "dejala por fuera",
          "match": "contains"
        },
        {
          "phrase": "no lo agregues",
          "match": "contains"
        },
        {
          "phrase": "no la agregues",
          "match": "contains"
        },
        {
          "phrase": "descartalo",
          "match": "contains"
        },
        {
          "phrase": "descartala",
          "match": "contains"
        }
      ],
      "quantityCorrectionPhrases": [
        "dame",
        "agregame",
        "agrega",
        "ponme",
        "pon",
        "dejame",
        "deja"
      ],
      "discardAllOnExplicitFinalization": true
    },
    "matching": {
      "exactNameDominanceMinimumMatches": 2,
      "candidateMentionSimilarity": 0.8,
      "pendingReferenceSimilarity": 0.78,
      "candidateSelectionSimilarity": 0.6
    }
  },
  "operatingHours": {
    "enforce": false,
    "outsideHours": {
      "guidance": "Responde de forma breve, cordial y cerrada. Explica que el negocio esta fuera de horario y que el proximo horario habil es {{next_operating_window}}. Adapta el mensaje a lo que dijo el cliente, pero no solicites datos, no prometas ejecutar gestiones, no abras catalogos y no termines con preguntas."
    }
  },
  "conversationFollowUp": {
    "enabled": true,
    "delayMinutes": 120,
    "guidance": "Retoma con calidez y brevedad la pregunta, eleccion o confirmacion concreta que sigue pendiente en el pedido. Usa el contexto vigente y formula una sola pregunta enfocada. No repitas catalogos, carritos ni resumenes completos; no agregues urgencia, descuentos, disponibilidad inventada ni promesas, y no modifiques el pedido.",
    "respectOperatingHours": true
  },
  "persona": "Eres el asistente comercial de DISTRIBUCIONES ANDINA SANTANDER por WhatsApp. Atiendes pedidos de alimentos y productos de consumo para hogares y negocios. Hablas en espanol de forma cercana, empatica, natural y servicial, como una persona atenta que acompana al cliente a armar su pedido. Usas parrafos cortos y espacios en blanco para que el mensaje sea facil de leer en WhatsApp. Evitas sonar como formulario, menu automatico o instruccion rigida. Puedes usar un emoji amable de manera ocasional, sin exagerar. El saludo inicial y el cierre son los momentos para usar el nombre del cliente; en los turnos intermedios respondes directamente. El catalogo y los resultados de las operaciones son la fuente de verdad comercial.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\n## PRESENTACION\n\n- Presentate como asistente de DISTRIBUCIONES ANDINA SANTANDER con tono breve, amable y practico.\n- Reserva el nombre del cliente para el saludo inicial y el cierre; en los turnos intermedios responde directamente.\n- Presenta catalogos, precios, carrito, totales y estado del pedido exclusivamente desde resultados oficiales del turno.",
  "messageSequences": {
    "order_created_customer": {
      "messages": [
        {
          "body": "Gracias por tu pedido, {customer_name}. Lo recibimos correctamente y ya estamos coordinando la entrega."
        }
      ]
    },
    "order_created": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "order_created",
          "language": "es_CO",
          "bodyParameters": [
            "{order_number}",
            "{customer_name}",
            "{customer_phone}",
            "{city}",
            "{delivery_address}",
            "{items}",
            "{total}",
            "{currency}"
          ]
        }
      ]
    },
    "manual_payment_approval_request": {
      "messages": [
        {
          "type": "text",
          "body": "*Pago manual pendiente*\n\nPedido: {order_number}\nCliente: {customer_name}\nTelefono: {customer_phone}\nEntrega: {delivery_address}\nProductos: {items}\nTotal: ${amount} {currency}\n\nValida el pago antes de confirmarlo.",
          "buttons": [
            {
              "id": "manual_payment:confirm:{payment_transaction_id}",
              "title": "Confirmar pago"
            }
          ]
        }
      ]
    }
  },
  "globalActions": [
    {
      "id": "human_handoff",
      "priority": 1000,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja, caso mayorista especial o solicitud fuera del alcance.",
      "conversationGuidance": "Detecta ?nicamente una solicitud expl?cita de atenci?n humana, una queja que requiera intervenci?n o una negociaci?n especial fuera del alcance configurado.",
      "signal": {
        "type": "human_escalation",
        "description": "Solicitud expl?cita de hablar con una persona, queja que requiere intervenci?n o negociaci?n comercial especial fuera del alcance.",
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
    },
    {
      "id": "cart_mutation",
      "priority": 875,
      "goal": "Aplicar cambios explicitos al unico carrito activo desde cualquier etapa, sin depender del checkpoint conversacional.",
      "conversationGuidance": "Detecta order_changes solo ante una instruccion explicita de agregar, quitar o cambiar cantidades. La consulta de opciones pertenece a catalog_query. Esta capacidad es el fallback transversal; una stage que declare la misma senal tiene precedencia y es su unico propietario durante ese turno.",
      "signal": {
        "type": "order_changes",
        "description": "Una mutacion explicita de uno o varios productos del unico pedido activo. Representa cada producto afectado con exactamente un comando: add cuando la cantidad es incremental o corresponde a un producto nuevo; set_quantity cuando la cantidad expresa el total final deseado para una linea existente; remove cuando se elimina por completo la linea, con quantity nulo. Cada comando corresponde a un producto afectado en el mensaje actual y se emite exactamente una vez. Conserva referencias parciales o contextuales, todas las cantidades y todos los productos del turno; el historial se usa para resolver la referencia, mientras las mutaciones provienen del mensaje actual. El motor resuelve catalogo, ambiguedad e inventario de forma autoritativa. Cuando exista una seleccion pendiente, la referencia elegida continua esa misma mutacion y el motor restaura el resto del lote.",
        "valueSchema": {
          "type": "array",
          "items": {
            "anyOf": [
              {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": {
                    "type": "string",
                    "enum": [
                      "add"
                    ]
                  },
                  "productText": {
                    "type": "string"
                  },
                  "quantity": {
                    "type": "number"
                  },
                  "destinationReference": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "operation",
                  "productText",
                  "quantity",
                  "destinationReference"
                ]
              },
              {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": {
                    "type": "string",
                    "enum": [
                      "set_quantity"
                    ]
                  },
                  "productText": {
                    "type": "string"
                  },
                  "quantity": {
                    "type": "number"
                  },
                  "destinationReference": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "operation",
                  "productText",
                  "quantity",
                  "destinationReference"
                ]
              },
              {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": {
                    "type": "string",
                    "enum": [
                      "remove",
                      "cancel_pending"
                    ]
                  },
                  "productText": {
                    "type": "string"
                  },
                  "quantity": {
                    "type": "null"
                  },
                  "destinationReference": {
                    "type": [
                      "string",
                      "null"
                    ]
                  }
                },
                "required": [
                  "operation",
                  "productText",
                  "quantity",
                  "destinationReference"
                ]
              }
            ]
          }
        },
        "ambiguityRules": [
          {
            "type": "distinct_values",
            "valueProperty": "destinationReference",
            "field": "delivery_address",
            "minimumDistinctValues": 2
          }
        ]
      },
      "actions": [
        {
          "id": "apply_order_changes",
          "operation": "commerce.apply_order_changes",
          "trigger": "on_signal",
          "signal": "order_changes",
          "arguments": {
            "commands": "{{signal.order_changes.value}}"
          },
          "onOutcome": {
            "cart.applied": {
              "response": {
                "guidance": "Confirma brevemente los cambios aplicados y continua segun el objetivo de la etapa."
              },
              "effects": [
                {
                  "type": "facts.clear",
                  "facts": [
                    "order_finalized",
                    "order_checkout_presented",
                    "customer_confirmed"
                  ]
                },
                {
                  "type": "presentation.add",
                  "template": "cart_snapshot",
                  "dataPath": "order",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "cart.pending_cancelled": {
              "response": {
                "guidance": "Si discarded_items contiene productos, indica brevemente cuales referencias sin existencia o sin coincidencia segura se dejaron fuera y continua inmediatamente con el cierre o el objetivo de la etapa, sin pedir otra confirmacion. Para otras cancelaciones, confirma brevemente la seleccion cancelada."
              }
            },
            "cart.product_not_found": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Indica que ese producto no se encontro y pide una descripcion o referencia mas precisa."
              }
            },
            "cart.product_ambiguous": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Presenta unicamente los candidatos devueltos y pregunta cual referencia desea."
              },
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "product_ambiguity",
                  "dataPath": "error.context",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "cart.insufficient_stock": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Explica con claridad la cantidad disponible y pide una cantidad valida; ningun cambio del lote fue aplicado."
              },
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "insufficient_stock",
                  "dataPath": "error.context",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "cart.item_not_found_or_ambiguous": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Aclara cual producto existente del pedido desea modificar."
              }
            },
            "cart.conflicting_commands": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "Pide aclarar el cambio final para el producto repetido; no se aplico ningun cambio del lote."
              }
            },
            "cart.multiple_destinations": {
              "response": {
                "mode": "ask_clarification",
                "guidance": "No se aplico ningun cambio. Pregunta cual direccion debe usarse para entregar todo el unico pedido."
              }
            }
          },
          "execution": {
            "idempotency": "none"
          }
        }
      ]
    },
    {
      "id": "known_fact_lookup",
      "priority": 860,
      "goal": "Responder preguntas del cliente sobre datos conversacionales ya persistidos que la configuracion autoriza revelar.",
      "conversationGuidance": "Detecta known_fact_query cuando el cliente pregunta cual valor suyo o de su solicitud esta registrado, vigente o guardado. Resuelve referencias breves desde la pregunta inmediatamente anterior. Solo solicita claves incluidas en el enum y nunca uses esta senal para buscar productos, ejecutar cambios ni revelar facts tecnicos.",
      "signal": {
        "type": "known_fact_query",
        "description": "Consulta de solo lectura sobre uno o varios datos del cliente o de la solicitud que ya estan persistidos y autorizados para mostrarse.",
        "valueSchema": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "fact_keys": {
              "type": "array",
              "items": {
                "type": "string",
                "enum": [
                  "customer_name",
                  "customer_type",
                  "delivery_method",
                  "city",
                  "delivery_address",
                  "delivery_reference",
                  "delivery_recipient_name",
                  "delivery_phone",
                  "payment_method"
                ]
              },
              "minItems": 1,
              "maxItems": 3
            }
          },
          "required": [
            "fact_keys"
          ]
        }
      },
      "actions": [
        {
          "id": "show_known_facts",
          "operation": "conversation.get_known_facts",
          "execution": {
            "idempotency": "none"
          },
          "trigger": "on_signal",
          "signal": "known_fact_query",
          "arguments": {
            "fact_keys": "{{signal.known_fact_query.value.fact_keys}}"
          },
          "onOutcome": {
            "known_facts.found": {
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "known_facts",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "known_facts.not_found": {
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "known_facts_missing",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            }
          }
        }
      ]
    },
    {
      "id": "catalog_lookup",
      "priority": 850,
      "goal": "Consultar el catalogo oficial cuando el cliente pregunte por productos, disponibilidad, referencias, precios u opciones, sin depender de la etapa activa.",
      "conversationGuidance": "Responde las consultas de mercancia comprable exclusivamente con resultados autoritativos del catalogo. Para una exploracion abierta, presenta las categorias retornadas y ayuda al cliente a elegir una o pedir un producto concreto. Para resultados paginados, conserva el contexto de la consulta activa. Nunca inventes disponibilidad, categorias, nombres ni precios.",
      "signal": {
        "type": "catalog_query",
        "description": "Emite esta senal cuando el cliente quiere explorar o buscar mercancia comprable. Asigna mode=categories a una exploracion abierta, mode=search a un producto, necesidad, ingrediente o categoria concreta, y mode=continue cuando pide la pagina siguiente del conjunto activo; una continuacion conserva queries vacio. Puede coexistir con order_changes. No la emitas para recuperar datos de entrega, direccion, recogida, pago, identidad, perfil, cliente u orden. Si rechaza una referencia del carrito y pide alternativas, conserva esa referencia en replacement_reference.",
        "valueSchema": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "queries": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "minItems": 0
            },
            "mode": {
              "type": "string",
              "enum": [
                "categories",
                "search",
                "continue"
              ]
            },
            "replacement_reference": {
              "description": "Referencia original del carrito que el cliente rechazo y desea sustituir con esta busqueda.",
              "type": [
                "string",
                "null"
              ]
            }
          },
          "required": [
            "queries",
            "mode",
            "replacement_reference"
          ]
        }
      },
      "actions": [
        {
          "id": "search_catalog_request",
          "operation": "commerce.search_products",
          "execution": {
            "idempotency": "none"
          },
          "trigger": "on_signal",
          "signal": "catalog_query",
          "arguments": {
            "queries": "{{signal.catalog_query.value.queries}}",
            "mode": "{{signal.catalog_query.value.mode}}",
            "replacement_reference": "{{signal.catalog_query.value.replacement_reference}}",
            "limit": 10
          },
          "onOutcome": {
            "categories.not_found": {
              "response": {},
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "catalog_no_results",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "categories.found": {
              "response": {},
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "catalog_categories",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "catalog.no_more": {
              "response": {},
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "catalog_no_more",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "products.not_found": {
              "response": {},
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "catalog_no_results",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            },
            "products.found": {
              "response": {},
              "effects": [
                {
                  "type": "presentation.add",
                  "template": "catalog_results",
                  "mode": "Exclusive",
                  "priority": "Required"
                }
              ]
            }
          }
        }
      ]
    },
    {
      "id": "restart_order_request",
      "priority": 900,
      "goal": "Iniciar una solicitud de pedido nueva cuando el cliente indique inequivocamente que abandona la solicitud activa y quiere comenzar otra.",
      "conversationGuidance": "Detecta restart_request solo ante intencion explicita de comenzar un pedido nuevo o empezar de nuevo. Tambien aplica cuando ya se finalizo la seleccion del pedido activo y el cliente vuelve a saludar diciendo inequivocamente que quiere hacer un pedido. No lo detectes por un saludo solo, por ajustes al carrito vigente, por consultas de productos ni por expresiones de finalizacion como solo eso.",
      "signal": {
        "type": "restart_request",
        "description": "El cliente solicita abandonar la solicitud de pedido activa y comenzar un pedido nuevo desde cero.",
        "valueSchema": {
          "type": "boolean"
        }
      },
      "actions": [
        {
          "id": "reset_order_request",
          "operation": "conversation.reset_request",
          "trigger": "on_signal",
          "signal": "restart_request",
          "arguments": {},
          "execution": {
            "idempotency": "none"
          }
        }
      ]
    }
  ],
  "factSchema": [
    {
      "key": "customer_name",
      "role": "customer.name",
      "label": "nombre del cliente o establecimiento",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "customer",
      "extractionGuidance": "Representa exclusivamente la identidad del cliente o establecimiento que realiza la compra. No lo actualices con el nombre de quien recibe, un contacto de entrega, un beneficiario ni otra persona mencionada con un rol diferente."
    },
    {
      "key": "customer_type",
      "role": "customer.type",
      "label": "perfil del cliente",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "customer",
      "options": [
        {
          "label": "Hogar",
          "selector": "A",
          "value": "Hogar"
        },
        {
          "label": "Tienda o minimercado",
          "selector": "B",
          "value": "TiendaMinimercado"
        },
        {
          "label": "Restaurante",
          "selector": "C",
          "value": "Restaurante"
        },
        {
          "label": "Comida rapida",
          "selector": "D",
          "value": "ComidaRapida"
        },
        {
          "label": "Distribuidor",
          "selector": "E",
          "value": "Distribuidor"
        }
      ]
    },
    {
      "key": "order_finalized",
      "role": "order.finalized",
      "label": "cliente finalizo el carrito",
      "type": "boolean",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Representa cualquier intencion clara del cliente de conservar el carrito actual y continuar con el pedido, sin depender de palabras exactas. Incluye expresiones equivalentes a terminar, no agregar mas, dejarlo asi o seguir con lo actual, aunque existan referencias pendientes que quedaran fuera."
    },
    {
      "key": "delivery_method",
      "role": "shipping.method",
      "label": "modalidad de entrega",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Normaliza la modalidad elegida al valor canonico configurado para entrega o recogida.",
      "options": [
        {
          "value": "domicilio",
          "label": "Domicilio"
        },
        {
          "value": "recogida",
          "label": "Recogida"
        }
      ]
    },
    {
      "key": "city",
      "role": "shipping.city",
      "label": "ciudad de entrega",
      "type": "string",
      "required": true,
      "source": "system",
      "customerReadable": true,
      "defaultValue": "Valledupar",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "delivery_address",
      "role": "shipping.address",
      "label": "direccion de entrega o recogida",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Extrae solo la ubicacion fisica. Si el mismo mensaje incluye telefono o celular, excluye de la direccion el numero telefonico y expresiones de enlace como y el telefono es, y el numero es o variantes con errores ortograficos."
    },
    {
      "key": "delivery_reference",
      "role": "shipping.reference",
      "label": "barrio, apartamento o referencia complementaria de entrega",
      "type": "string",
      "required": false,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Extrae solo detalles complementarios para localizar la entrega, como barrio, urbanizacion, apartamento, interior, bloque, indicaciones o un punto de referencia. No copies el telefono ni el nombre del receptor."
    },
    {
      "key": "delivery_recipient_name",
      "role": "shipping.recipient_name",
      "label": "nombre de quien recibe el pedido",
      "type": "string",
      "required": false,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Extrae este dato solo cuando el mensaje identifica a una persona como quien recibe, receptor o contacto de entrega. Nunca lo conviertas en customer_name ni asumas que cambia la identidad del cliente."
    },
    {
      "key": "delivery_phone",
      "role": "customer.phone",
      "label": "celular de entrega",
      "type": "phone",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "customer"
    },
    {
      "key": "payment_method",
      "role": "payment.method",
      "label": "metodo de pago",
      "type": "string",
      "required": true,
      "source": "user",
      "customerReadable": true,
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Normaliza la eleccion al metodo de pago canonico configurado.",
      "options": [
        {
          "value": "efectivo",
          "label": "Efectivo"
        },
        {
          "value": "transferencia",
          "label": "Transferencia"
        },
        {
          "value": "datafono",
          "label": "Datáfono"
        }
      ]
    },
    {
      "key": "order_checkout_presented",
      "role": "order.checkout_presented",
      "label": "resumen final presentado",
      "type": "boolean",
      "required": false,
      "source": "system",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "system.recipe_catalog_queries",
      "role": "system.recipe_catalog_queries",
      "label": "consultas de catalogo derivadas de receta",
      "type": "json",
      "required": false,
      "source": "system",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "customer_confirmed",
      "role": "confirmation.verbal",
      "label": "confirmacion verbal del pedido",
      "type": "boolean",
      "required": false,
      "source": "user",
      "scope": "request",
      "dependsOn": [
        "order_checkout_presented",
        "delivery_method",
        "city",
        "delivery_address",
        "delivery_reference",
        "delivery_recipient_name",
        "delivery_phone",
        "customer_name",
        "payment_method"
      ],
      "retentionDays": 1,
      "extractionGuidance": "Representa la confirmacion explicita del resumen final vigente."
    }
  ],
  "notifications": {
    "order_created": {
      "enabled": true,
      "deliveries": [
        {
          "id": "customer",
          "enabled": true,
          "recipients": [
            "source:conversation"
          ],
          "sendMessageSequence": "order_created_customer"
        },
        {
          "id": "internal",
          "enabled": true,
          "recipients": [
            "inbound:payment_approver"
          ],
          "sendMessageSequence": "order_created"
        }
      ]
    },
    "manual_payment_requested": {
      "enabled": true,
      "deliveries": [
        {
          "id": "internal",
          "enabled": true,
          "recipients": [
            "inbound:payment_approver"
          ],
          "sendMessageSequence": "manual_payment_approval_request"
        }
      ]
    }
  },
  "webhooks": {
    "wompi": {}
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
    "modes": {
      "order": {
        "requiredFactRoles": {},
        "paymentMethods": {
          "efectivo": {
            "label": "efectivo al recibir",
            "aliases": [
              "efectivo",
              "contraentrega"
            ],
            "template": "order_checkout_no_payment"
          },
          "datafono": {
            "label": "datafono al recibir",
            "aliases": [
              "datafono",
              "datáfono",
              "tarjeta",
              "pago con tarjeta"
            ],
            "template": "order_checkout_card_terminal"
          },
          "transferencia": {
            "label": "transferencia manual",
            "aliases": [
              "transferencia",
              "nequi",
              "bancolombia"
            ],
            "template": "order_checkout_manual_transfer",
            "manualConfirmationRequired": true,
            "manualExpirationMinutes": 1440,
            "confirmationOutcome": "order_paid"
          }
        },
        "shipping": {
          "enabled": true,
          "localCity": "Valledupar",
          "localCost": 6000,
          "nationalCost": 25000
        }
      }
    }
  },
  "conversationOpening": {
    "enabled": true,
    "guidance": "Escribe una sola bienvenida calida como primer parrafo: saluda, da la bienvenida a DISTRIBUCIONES ANDINA SANTANDER y expresa que es un gusto saludarle. Si conoces el nombre del cliente, usalo una sola vez; si no lo conoces, no inventes ninguno. Puedes usar uno o dos emojis naturales. No digas ''aqui estoy para lo que necesites'' ni hagas preguntas en este primer parrafo. No menciones el tipo de cliente, ciudad, direccion, telefono, compras anteriores ni otros datos recordados. La continuacion, separada por una linea en blanco, debe seguir el objetivo de la etapa.",
    "allowQuestions": false
  },
  "failureResponses": {
    "llmUnavailable": "Lo siento, en este momento tengo un inconveniente temporal para procesar tu mensaje. Por favor, intenta nuevamente en unos minutos."
  },
  "templates": {
    "order_checkout_no_payment": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: {{customer_name}}\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: efectivo al recibir\n\nConfirmas tu pedido con esta informacion?",
    "order_checkout_card_terminal": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: {{customer_name}}\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: datafono al recibir\n\nLlevaremos el datafono para realizar el pago al momento de la entrega. Confirmas tu pedido con esta informacion?",
    "order_checkout_manual_transfer": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: {{customer_name}}\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: transferencia manual\n\nTu pago queda pendiente de confirmacion manual. Un agente del equipo de DISTRIBUCIONES ANDINA SANTANDER confirmara el pago; cuando se confirme, te notificaremos que el pedido fue creado.",
    "catalog_results": "Encontré estas opciones:\r\n\r\n*Productos disponibles*\r\n\r\n{{#each products}}\r\n- {{name}}: ${{unit_price}} {{currency}}\r\n{{/each}}\r\n{{#each recommendations}}\r\n\r\n*También podría servirte*\r\n- {{name}}: ${{unit_price}} {{currency}}\r\n{{#if reason}}{{reason}}\r\n{{/if}}{{/each}}\r\n\r\n¿Cuál te interesa y cuántas unidades necesitas?",
    "catalog_categories": "Podemos empezar por alguna de estas categorías:\r\n\r\n{{#each categories}}\r\n- {{name}}{{/each}}\r\n\r\nElige una para ver sus productos o dime directamente qué estás buscando.",
    "catalog_no_more": "Esas son todas las opciones que encontré para esta búsqueda. Puedes elegir una o decirme qué otro producto necesitas.",
    "catalog_no_results": "No me apareció {{#if search_text}}{{search_text}}{{else}}ningún producto para esa búsqueda{{/if}} en el catálogo actual.\r\n\r\nSi me das otra marca, presentación o referencia, lo intentamos de nuevo.",
    "known_facts": "Esto es lo que tengo registrado:\r\n\r\n{{#each facts}}\r\n- {{label}}: {{value}}\r\n{{/each}}",
    "known_facts_missing": "Todavía no tengo ese dato. Puedes enviármelo o corregirlo cuando quieras.",
    "recipe_results": "Buena idea. Puedes inspirarte con estas preparaciones:\r\n\r\n*Ideas para preparar*\r\n{{#each results}}\r\n- {{Title}}\r\n  {{Url}}\r\n{{/each}}",
    "cart_snapshot": "Listo, así va tu pedido 🙌\r\n\r\n{{#each items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\n¿Quieres agregar algo más? Cuando termines, dime que eso es todo.",
    "product_ambiguity": "Encontré varias opciones para {{product_text}}:\r\n\r\n{{#each product_options}}\r\n- {{Name}}: ${{UnitPrice}} {{Currency}}\r\n{{/each}}\r\n\r\n¿Cuál prefieres? El resto de tu pedido queda igual.",
    "insufficient_stock": "De {{product_text}} no alcanza la cantidad que pediste.\r\n\r\n- Pediste: {{requested_quantity}}\r\n- Hay disponibles: {{available_quantity}}\r\n\r\nPuedes pedir hasta {{maximum_command_quantity}}. ¿Con cuántas unidades lo dejamos?"
  },
  "flows": [
    {
      "id": "order",
      "type": "primary",
      "routingGuidance": "Use this primary flow for DISTRIBUCIONES ANDINA SANTANDER product orders, customer identification, profile classification, catalog-grounded recommendations, delivery data, payment method and order confirmation.",
      "stages": [
        {
          "id": "customer_name",
          "name": "Identificacion del cliente",
          "leadQualification": { "band": "exploring", "priority": 10, "label": "Identificación iniciada" },
          "goal": "Obtener el nombre del cliente o establecimiento antes de iniciar el pedido cuando no exista un nombre confiable.",
          "response": {},
          "advanceWhenFacts": [
            "customer_name"
          ],
          "conversationGuidance": "Si falta customer_name y el cliente no lo informo en el mensaje actual, explica con cercania que puedes ayudarle con su pedido y solicita su nombre o el nombre de su negocio. No repitas el saludo ni la bienvenida en la continuacion. Separa la explicacion y la pregunta en parrafos cortos. Si ya lo dijo, continua sin volver a pedirlo; el motor registra el dato extraido.",
          "collect": [
            "customer_name",
            "customer_type"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "customer_type",
          "name": "Perfil del cliente",
          "goal": "Clasificar el perfil comercial como Hogar, TiendaMinimercado, Restaurante, ComidaRapida o Distribuidor.",
          "response": {},
          "advanceWhenFacts": [
            "customer_type"
          ],
          "conversationGuidance": "Si falta customer_type, explica brevemente que conocer el perfil permite atender mejor la compra. Presenta las opciones en una lista legible, indica que puede responder con la letra o con el nombre y registra el valor canonico. No agradezcas ni confirmes el nombre salvo que el cliente lo haya proporcionado o corregido en el mensaje actual. Si el cliente corrige el perfil posteriormente, actualizalo.",
          "collect": [
            "customer_type"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "product_selection",
          "name": "Productos, catalogo y recomendaciones",
          "leadQualification": { "band": "interested", "priority": 35, "label": "Productos de interés" },
          "goal": "Recibir pedidos abiertos, resolver productos reales del catalogo, recomendar de forma controlada y construir el carrito hasta que el cliente finalice.",
          "response": {},
          "advanceWhenFacts": [
            "order_finalized"
          ],
          "conversationGuidance": "Acompana al cliente de forma cercana mientras elige productos. Al abrir una solicitud sin una consulta o seleccion concreta, explica simplemente que estas para ayudarle con su pedido y pregunta que desea el dia de hoy, sin repetir la bienvenida ni mencionar su perfil, ubicacion o categorias supuestas. Las consultas comerciales se presentan con resultados autoritativos del catalogo. Elegir una referencia ofrecida por una consulta no la agrega al pedido: si aun no hay cantidad, pregunta cuantas unidades desea y nunca supongas una unidad. Cuando el cliente indique la cantidad, conserva la referencia elegida desde la conversacion inmediata y aplica un unico cambio. Las solicitudes de preparacion producen ideas de receta y productos relacionados en el mismo turno. Cuando solicite productos y cantidades, conserva el lote completo para que el motor lo aplique al unico pedido activo. Tras cada cambio presenta el estado vigente con una transicion natural. Cuando el cliente exprese semanticamente que desea conservar el carrito actual y continuar, registra order_finalized=true sin exigir una frase especifica; las referencias aun pendientes quedaran fuera y no deben impedir el avance.",
          "collect": [
            "order_finalized",
            "delivery_method",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "payment_method"
          ],
          "signals": [
            {
              "type": "recipe_request",
              "description": "Solicitud de ideas para preparar una comida. El valor contiene el ingrediente o la preparacion principal que debe buscarse.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "order_changes",
              "description": "Una mutacion explicita de uno o varios productos del unico pedido activo. Representa cada producto afectado con exactamente un comando: add cuando la cantidad es incremental o corresponde a un producto nuevo; set_quantity cuando la cantidad expresa el total final deseado para una linea existente; remove cuando se elimina por completo la linea, con quantity nulo. Cada comando corresponde a un producto afectado en el mensaje actual y se emite exactamente una vez. Conserva referencias parciales o contextuales, todas las cantidades y todos los productos del turno; el historial se usa para resolver la referencia, mientras las mutaciones provienen del mensaje actual. El motor resuelve catalogo, ambiguedad e inventario de forma autoritativa. Cuando exista una seleccion pendiente, la referencia elegida continua esa misma mutacion y el motor restaura el resto del lote.",
              "valueSchema": {
                "type": "array",
                "items": {
                  "anyOf": [
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "add"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "number"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    },
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "set_quantity"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "number"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    },
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "operation": {
                          "type": "string",
                          "enum": [
                            "remove",
                            "cancel_pending"
                          ]
                        },
                        "productText": {
                          "type": "string"
                        },
                        "quantity": {
                          "type": "null"
                        },
                        "destinationReference": {
                          "type": [
                            "string",
                            "null"
                          ]
                        }
                      },
                      "required": [
                        "operation",
                        "productText",
                        "quantity",
                        "destinationReference"
                      ]
                    }
                  ]
                }
              },
              "ambiguityRules": [
                {
                  "type": "distinct_values",
                  "valueProperty": "destinationReference",
                  "field": "delivery_address",
                  "minimumDistinctValues": 2
                }
              ]
            }
          ],
          "actions": [
            {
              "id": "search_recipe_request",
              "operation": "commerce.search_recipes",
              "execution": {
                "idempotency": "none"
              },
              "trigger": "on_signal",
              "signal": "recipe_request",
              "arguments": {
                "ingredient": "{{signal.recipe_request.value}}",
                "query": "preparacion facil",
                "limit": 2
              },
              "onOutcome": {
                "recipes.found": {
                  "effects": [
                    {
                      "type": "facts.set_from_outcome",
                      "bindings": {
                        "system.recipe_catalog_queries": "catalog_search_queries"
                      }
                    },
                    {
                      "type": "presentation.add",
                      "template": "recipe_results",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ],
                  "response": {
                    "guidance": "Presenta mÃ¡ximo dos ideas devueltas y luego muestra Ãºnicamente ingredientes encontrados en el catÃ¡logo oficial."
                  }
                }
              }
            },
            {
              "id": "search_recipe_catalog_products",
              "operation": "commerce.search_products",
              "execution": {
                "idempotency": "none"
              },
              "trigger": "when_ready",
              "condition": {
                "factPresent": "system.recipe_catalog_queries"
              },
              "arguments": {
                "queries": "{{fact.system.recipe_catalog_queries}}",
                "mode": "search",
                "limit": 10
              },
              "onOutcome": {
                "products.not_found": {
                  "response": {},
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "system.recipe_catalog_queries"
                      ]
                    },
                    {
                      "type": "presentation.add",
                      "template": "catalog_no_results",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "products.found": {
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "system.recipe_catalog_queries"
                      ]
                    },
                    {
                      "type": "presentation.add",
                      "template": "catalog_results",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ],
                  "response": {
                    "guidance": "Muestra solo productos reales devueltos por catÃ¡logo, con presentaciÃ³n y precio cuando estÃ©n disponibles."
                  }
                }
              }
            },
            {
              "id": "apply_order_changes",
              "operation": "commerce.apply_order_changes",
              "trigger": "on_signal",
              "signal": "order_changes",
              "arguments": {
                "commands": "{{signal.order_changes.value}}"
              },
              "onOutcome": {
                "cart.applied": {
                  "response": {
                    "guidance": "Confirma brevemente los cambios aplicados y continÃºa segÃºn el objetivo de la etapa."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "cart_snapshot",
                      "dataPath": "order",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.pending_cancelled": {
                  "response": {
                    "guidance": "Si discarded_items contiene productos, indica brevemente cuales referencias sin existencia o sin coincidencia segura se dejaron fuera y continua inmediatamente con el cierre o el objetivo de la etapa, sin pedir otra confirmacion. Para otras cancelaciones, confirma brevemente la seleccion cancelada."
                  }
                },
                "cart.product_not_found": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Indica que ese producto no se encontrÃ³ y pide una descripciÃ³n o referencia mÃ¡s precisa."
                  }
                },
                "cart.product_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Presenta Ãºnicamente los candidatos devueltos y pregunta cuÃ¡l referencia desea."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "product_ambiguity",
                      "dataPath": "error.context",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.insufficient_stock": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Explica con claridad la cantidad disponible y pide una cantidad valida; ningun cambio del lote fue aplicado."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "insufficient_stock",
                      "dataPath": "error.context",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "cart.item_not_found_or_ambiguous": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Aclara cuÃ¡l producto existente del pedido desea modificar."
                  }
                },
                "cart.conflicting_commands": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "Pide aclarar el cambio final para el producto repetido; no se aplicÃ³ ningÃºn cambio del lote."
                  }
                },
                "cart.multiple_destinations": {
                  "response": {
                    "mode": "ask_clarification",
                    "guidance": "No se aplicÃ³ ningÃºn cambio. Pregunta cuÃ¡l direcciÃ³n debe usarse para entregar todo el Ãºnico pedido."
                  }
                }
              }
            }
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "order_data",
          "name": "Entrega",
          "leadQualification": { "band": "high_intent", "priority": 55, "label": "Datos de entrega" },
          "goal": "Definir recogida o domicilio y obtener solo los datos faltantes requeridos por el checkout.",
          "response": {},
          "advanceWhenFacts": [
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_phone",
            "customer_name"
          ],
          "reentryOnFactChanged": [
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "customer_name"
          ],
          "conversationGuidance": "Despues de que el cliente termine de agregar productos pregunta unicamente si prefiere recogida o domicilio, sin mostrar un resumen intermedio. Deja esa eleccion en un mensaje separado. Para recogida, registra la modalidad y el punto configurado como direccion. Cuando elija domicilio, solicita todos los datos faltantes en un solo mensaje breve y estructurado con esta lista: direccion completa; barrio, apartamento o referencia complementaria; nombre de quien recibe; y celular de entrega. Pide solo los que falten, permite responder todo junto y no envies una pregunta separada por cada dato. La referencia complementaria sigue siendo opcional: puede responder no aplica y nunca debe detener el flujo. delivery_recipient_name identifica a quien recibe y nunca reemplaza customer_name. Usa la ciudad por defecto configurada salvo que el cliente indique otra.",
          "collect": [
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "payment_method"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "payment_method",
          "name": "Metodo de pago",
          "goal": "Elegir uno de los metodos de pago configurados para DISTRIBUCIONES ANDINA SANTANDER.",
          "response": {},
          "advanceWhenFacts": [
            "payment_method"
          ],
          "conversationGuidance": "Cuando la modalidad de entrega y los datos requeridos esten completos, pregunta como desea realizar el pago y presenta en una lista breve las tres opciones configuradas: efectivo, transferencia o datafono. Registra exactamente payment_method=efectivo, payment_method=transferencia o payment_method=datafono segun responda. No menciones metodos no configurados.",
          "collect": [
            "payment_method"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "summary",
          "name": "Resumen final del pedido",
          "leadQualification": { "band": "ready", "priority": 75, "label": "Pedido listo para confirmar" },
          "goal": "Preparar y mostrar el resumen oficial con entrega, pago y total final del motor.",
          "advanceWhenFacts": [
            "order_checkout_presented"
          ],
          "reentryOnFactChanged": [
            "order_finalized",
            "delivery_method",
            "city",
            "delivery_address",
            "delivery_reference",
            "delivery_recipient_name",
            "delivery_phone",
            "customer_name",
            "payment_method"
          ],
          "actions": [
            {
              "id": "prepare_order_checkout",
              "operation": "commerce.prepare_checkout",
              "trigger": "when_ready",
              "condition": {
                "all": [
                  {
                    "factPresent": "order_finalized"
                  },
                  {
                    "factPresent": "delivery_method"
                  },
                  {
                    "factPresent": "city"
                  },
                  {
                    "factPresent": "delivery_address"
                  },
                  {
                    "factPresent": "delivery_phone"
                  },
                  {
                    "factPresent": "customer_name"
                  },
                  {
                    "factPresent": "payment_method"
                  },
                  {
                    "factMissing": "order_checkout_presented"
                  }
                ]
              },
              "arguments": {},
              "onOutcome": {
                "order.checkout_ready": {
                  "response": {},
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                },
                "order.checkout_payment_required": {
                  "response": {},
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                },
                "order.checkout_pending_manual_payment": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                },
                "order_draft_missing": {
                  "response": {
                    "guidance": "Informa que no fue posible recuperar el pedido vigente y pide intentar nuevamente para continuar."
                  },
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "order_finalized",
                        "order_checkout_presented"
                      ]
                    }
                  ]
                },
                "missing_prerequisites": {
                  "response": {
                    "guidance": "Informa que faltan datos vigentes para preparar el resumen y solicita unicamente el siguiente dato requerido por la etapa."
                  },
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "order_finalized",
                        "order_checkout_presented"
                      ]
                    }
                  ]
                }
              }
            }
          ],
          "conversationGuidance": "Cuando ya existan items, carrito aprobado, entrega y metodo de pago, el motor prepara el checkout una sola vez. Si el metodo es efectivo, muestra el resumen autoritativo renderizado por el motor y pide confirmacion verbal. Si el metodo es transferencia, muestra el resumen autoritativo e informa que el pago queda pendiente de confirmacion manual por el equipo; no pidas comprobante ni confirmacion adicional. Si el metodo es datafono, muestra el resumen autoritativo, informa que se llevara el datafono para pagar al recibir y pide confirmacion verbal exactamente igual que con efectivo. Si falla por configuracion no recuperable, escala a humano.",
          "collect": [
            "order_checkout_presented"
          ],
          "awaitCustomerReply": true,
          "transitions": [
            {
              "id": "summary_to_manual_payment_pending",
              "priority": 20,
              "condition": {
                "all": [
                  {
                    "factPresent": "order_checkout_presented"
                  },
                  {
                    "factEquals": {
                      "key": "payment_method",
                      "value": "transferencia"
                    }
                  }
                ]
              },
              "to": "manual_payment_pending"
            },
            {
              "id": "summary_to_order_confirmation",
              "priority": 10,
              "condition": {
                "factPresent": "order_checkout_presented"
              },
              "to": "order_confirmation"
            }
          ]
        },
        {
          "id": "order_confirmation",
          "name": "Confirmacion del pedido",
          "leadQualification": { "band": "converted", "priority": 100, "label": "Pedido confirmado", "conversionOnRequestCompleted": true },
          "goal": "Crear el pedido despues de confirmacion del cliente.",
          "advanceWhenFacts": [
            "customer_confirmed"
          ],
          "actions": [
            {
              "id": "create_confirmed_delivery_payment_order",
              "operation": "commerce.create_order",
              "execution": {
                "idempotency": "once_per_request",
                "timeoutSeconds": 120,
                "maxAttempts": 1
              },
              "trigger": "when_ready",
              "condition": {
                "all": [
                  {
                    "any": [
                      {
                        "factEquals": {
                          "key": "payment_method",
                          "value": "efectivo"
                        }
                      },
                      {
                        "factEquals": {
                          "key": "payment_method",
                          "value": "datafono"
                        }
                      }
                    ]
                  },
                  {
                    "factEquals": {
                      "key": "customer_confirmed",
                      "value": true
                    }
                  }
                ]
              },
              "arguments": {
                "customer_confirmed": "{{fact.customer_confirmed}}"
              },
              "onOutcome": {
                "order.created": {
                  "effects": [
                    {
                      "type": "request.complete"
                    }
                  ],
                  "response": {
                    "suppressText": true
                  }
                }
              }
            }
          ],
          "conversationGuidance": "Si payment_method=transferencia, no pidas confirmacion verbal, no confirmes que el pedido fue creado y responde que el pago queda pendiente de confirmacion manual por el equipo de DISTRIBUCIONES ANDINA SANTANDER; cuando el pago se confirme manualmente, el sistema notificara que el pedido fue creado. Si payment_method=efectivo o payment_method=datafono y falta customer_confirmed, pide confirmacion verbal del resumen final y registrala solo cuando el cliente la entregue claramente. Con customer_confirmed=true y metodo efectivo o datafono, crea el pedido usando los facts vigentes. Para datafono, recuerda que se llevara el dispositivo y no afirmes que el pago ya fue recibido. Despues de crear el pedido envia la secuencia order_created_customer. Si corrige datos, metodo de pago o carrito, aplica el cambio y presenta resumen actualizado. No afirmes pago recibido solo por una imagen o comprobante si el workflow no lo valida.",
          "collect": [
            "customer_confirmed"
          ],
          "awaitCustomerReply": true
        },
        {
          "id": "manual_payment_pending",
          "name": "Pago pendiente de aprobacion",
          "goal": "Mantener la solicitud a la espera de la confirmacion manual del equipo sin pedir otra respuesta al cliente.",
          "conversationGuidance": "Informa brevemente que la transferencia sigue pendiente de validacion por el equipo y que se notificara el resultado. No solicites una confirmacion adicional.",
          "collect": [],
          "actions": [],
          "transitions": [],
          "response": {}
        }
      ]
    }
  ]
}';
SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_changes_applied',
    N'Hecho, tu pedido quedó actualizado:
{{#each applied_items}}
{{#if removed}}- Retiré {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} del carrito{{else}}- Agregué o actualicé {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}{{/if}}
{{/each}}

¿Quieres agregar algo más?');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_on_request',
    N'Así va tu pedido:

{{#each items}}
- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}
{{/each}}

*Total actual: ${{total}} {{currency}}*

¿Quieres agregar o cambiar algo más?');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, 'append $.globalActions', JSON_QUERY(N'{"id":"cart_review_request","priority":874,"goal":"Mostrar el carrito vigente cuando el cliente solicite verlo, sin mutarlo ni intentar resolver referencias pendientes.","conversationGuidance":"Emite cart_review_request ante cualquier solicitud de ver, revisar o saber como va el carrito o pedido actual. Es una consulta de solo lectura y nunca debe convertirse en order_changes.","signal":{"type":"cart_review_request","description":"Solicitud de solo lectura para presentar el carrito vigente.","valueSchema":{"type":"object","additionalProperties":false,"properties":{}}},"actions":[{"id":"show_current_cart","operation":"commerce.get_order_draft","trigger":"on_signal","signal":"cart_review_request","arguments":{},"onOutcome":{"order.draft_loaded":{"response":{"guidance":"Presenta el carrito vigente y pregunta si desea agregar o cambiar algo."},"effects":[{"type":"presentation.add","template":"cart_on_request","dataPath":"order","mode":"Exclusive","priority":"Required"}]},"order.draft_empty":{"response":{"guidance":"Indica brevemente que el carrito esta vacio y pregunta que desea agregar."}},"order_draft_missing":{"response":{"guidance":"Indica brevemente que aun no hay un carrito activo y pregunta que desea agregar."}}},"execution":{"idempotency":"none"}}]}'));
DECLARE @CartReviewGlobalActionIndex INT;
SELECT @CartReviewGlobalActionIndex = TRY_CONVERT(INT, [key])
FROM OPENJSON(@SettingsJson, '$.globalActions')
WHERE JSON_VALUE([value], '$.id') = 'cart_review_request';
IF @CartReviewGlobalActionIndex IS NULL
    THROW 51000, 'SeedAndinaSantander: accion global de consulta de carrito no encontrada.', 1;
DECLARE @CartReviewGlobalActionPath NVARCHAR(200) = CONCAT('$.globalActions[', @CartReviewGlobalActionIndex, ']');

DECLARE @CartAppliedOutcome NVARCHAR(MAX) = N'{"response":{"guidance":"Confirma unicamente los cambios aplicados y pregunta si desea agregar algo mas."},"effects":[{"type":"facts.clear","facts":["order_finalized","order_checkout_presented","customer_confirmed"]},{"type":"presentation.add","template":"cart_changes_applied","mode":"Exclusive","priority":"Required"}]}';DECLARE @PartialCartOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Da un resultado explicito para cada referencia del lote usando la presentacion deterministica: agregada, sin existencia, ambigua, sugerida, cantidad insuficiente o no encontrada. No omitas referencias ni las mezcles entre categorias."},"effects":[{"type":"presentation.add","template":"cart_partial","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductSuggestionOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Presenta la sugerencia devuelta y pide confirmacion explicita antes de agregarla."},"effects":[{"type":"presentation.add","template":"product_ambiguity","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductUnavailableOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Indica que la referencia identificada no esta disponible y solicita otra opcion; no afirmes que fue agregada."},"effects":[{"type":"presentation.add","template":"cart_product_unavailable","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductNotFoundOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Indica las referencias que no tuvieron coincidencia segura y solicita datos mas precisos; no afirmes que el carrito cambio."},"effects":[{"type":"presentation.add","template":"cart_not_found","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    N'Así quedó cada producto que pediste:\r\n{{#if applied_items}}\r\n*Agregados*\r\n{{#each applied_items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if unavailable_items}}\r\n*Sin existencia*\r\n{{#each unavailable_items}}\r\n- {{product_text}}{{#if recognized_name}} ({{recognized_name}}){{/if}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if insufficient_stock_items}}\r\n*Existencia insuficiente*\r\n{{#each insufficient_stock_items}}\r\n- {{product_text}}: solicitaste {{requested_quantity}} y hay {{available_quantity}}; puedes pedir hasta {{maximum_command_quantity}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if ambiguous_options}}\r\n*Necesito que elijas*\r\n{{#each ambiguous_options}}\r\n- Para {{product_text}}: {{name}} — ${{unit_price}} {{currency}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if suggested_options}}\r\n*Necesito confirmar*\r\n{{#each suggested_options}}\r\n- Para {{product_text}}: ¿te refieres a {{name}} — ${{unit_price}} {{currency}}?\r\n{{/each}}\r\n{{/if}}\r\n{{#if not_found_items}}\r\n*No encontrados*\r\n{{#each not_found_items}}\r\n- {{product_text}}\r\n{{/each}}\r\n{{/if}}\r\n*Pedido actual*\r\n{{#each items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\n{{#if can_finalize_with_pending}}Si eso es todo, dejaré fuera las referencias sin existencia o sin coincidencia segura. ¿Eso sería todo o deseas agregar algo más?{{else}}Indícame las elecciones o una referencia más precisa para los pendientes.{{/if}}');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'— ${{unit_price}} {{currency}}', N'— {{availability_text}}'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'{{product_text}}{{#if recognized_name}} ({{recognized_name}}){{/if}}', N'{{description}}'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'{{#if ambiguous_options}}\r\n*Necesito que elijas*\r\n{{#each ambiguous_options}}\r\n- Para {{product_text}}: {{name}} — {{availability_text}}\r\n{{/each}}\r\n{{/if}}',
        N'{{#if ambiguous_groups}}\r\n*Necesito que elijas*\r\n{{#each ambiguous_groups}}\r\nPara {{product_text}}, necesito que me confirmes una de estas opciones:\r\n{{options_text}}\r\n{{/each}}\r\n{{/if}}'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    REPLACE(JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'*Pedido actual*\r\n{{#each items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\n',
        N'*Total actual del pedido: ${{total}} {{currency}}*\r\n\r\n'));

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    CONCAT(N'{{#if is_pending_follow_up}}
Hecho, tu pedido quedó actualizado:
{{#each display_applied_items}}
{{#if removed}}- Retiré {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} del carrito{{else}}- Agregué o actualicé {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}{{/if}}
{{/each}}

{{#if can_finalize_with_pending}}Si eso es todo, dejaré fuera las referencias sin existencia o sin coincidencia segura. ¿Eso sería todo o deseas agregar algo más?{{else}}¿Quieres agregar algo más?{{/if}}
{{else}}
',
        JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'
{{/if}}'));
SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_not_found',
    N'No pude identificar con seguridad estas referencias:\r\n{{#each issues}}\r\n- {{ProductText}}\r\n{{/each}}\r\n\r\n{{#if can_finalize_with_pending}}Si eso es todo, las dejare fuera. ¿Eso sería todo o deseas agregar algo más?{{else}}Compárteme la marca, presentación, código o un nombre más preciso y la busco de nuevo.{{/if}}');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_product_unavailable',
    N'Encontré "{{product_text}}", pero en este momento no está disponible para agregarla.\r\n\r\n{{#if can_finalize_with_pending}}Si eso es todo, la dejare fuera. ¿Eso sería todo o deseas agregar algo más?{{else}}Tu pedido quedó igual. Si quieres, buscamos otra marca, presentación o producto.{{/if}}');

IF JSON_VALUE(@SettingsJson, '$.globalActions[1].actions[0].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedAndinaSantander: ruta global de carrito inesperada.', 1;
IF JSON_VALUE(@SettingsJson, '$.flows[0].stages[2].actions[2].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedAndinaSantander: ruta product_selection de carrito inesperada.', 1;

DECLARE @CartExecutionPaths TABLE (Path NVARCHAR(400) NOT NULL);
INSERT INTO @CartExecutionPaths (Path) VALUES
    (N'$.globalActions[1].actions[0].execution'),
    (N'$.flows[0].stages[2].actions[2].execution');

DECLARE @CartExecutionPath NVARCHAR(400);
DECLARE CartExecutionCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Path FROM @CartExecutionPaths;
OPEN CartExecutionCursor;
FETCH NEXT FROM CartExecutionCursor INTO @CartExecutionPath;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartExecutionPath,
        JSON_QUERY(N'{"idempotency":"input_version","timeoutSeconds":240,"maxAttempts":1}'));
    FETCH NEXT FROM CartExecutionCursor INTO @CartExecutionPath;
END;
CLOSE CartExecutionCursor;
DEALLOCATE CartExecutionCursor;

DECLARE @CartOutcomePaths TABLE (Path NVARCHAR(400) NOT NULL);
INSERT INTO @CartOutcomePaths (Path) VALUES
    (N'$.globalActions[1].actions[0].onOutcome'),
    (N'$.flows[0].stages[2].actions[2].onOutcome');

DECLARE @CartOutcomePath NVARCHAR(400);
DECLARE CartOutcomeCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Path FROM @CartOutcomePaths;
OPEN CartOutcomeCursor;
FETCH NEXT FROM CartOutcomeCursor INTO @CartOutcomePath;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.applied"', JSON_QUERY(@CartAppliedOutcome));
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.partially_applied"', JSON_QUERY(@PartialCartOutcome));
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.product_suggestion"', JSON_QUERY(@ProductSuggestionOutcome));
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.product_unavailable"', JSON_QUERY(@ProductUnavailableOutcome));
    SET @SettingsJson = JSON_MODIFY(@SettingsJson, @CartOutcomePath + N'."cart.product_not_found"', JSON_QUERY(@ProductNotFoundOutcome));
    FETCH NEXT FROM CartOutcomeCursor INTO @CartOutcomePath;
END
CLOSE CartOutcomeCursor;
DEALLOCATE CartOutcomeCursor;


IF ISJSON(@SettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedAndinaSantander: SettingsJson invalido.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, [Name], [Description], IsActive,
         SettingsJson, Model, Temperature, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Asistente DISTRIBUCIONES ANDINA SANTANDER',
         N'Asistente comercial para pedidos, clasificacion de cliente y recetas web para hogar.',
         1, @SettingsJson, N'gpt-4.1-mini', 0.2, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET BusinessId = @BusinessId,
        AgentTypeId = @AgentTypeId,
        [Name] = N'Asistente DISTRIBUCIONES ANDINA SANTANDER',
        [Description] = N'Asistente comercial para pedidos, clasificacion de cliente y recetas web para hogar.',
        IsActive = 1,
        SettingsJson = @SettingsJson,
        Model = N'gpt-4.1-mini',
        Temperature = 0.4,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END


SELECT @PlanId = SubscriptionPlanId FROM dbo.SubscriptionPlans WHERE Code = N'essential' AND IsActive = 1;
IF @PlanId IS NULL
    THROW 51000, 'SeedAndinaSantander: no existe el plan Esencial activo.', 1;

MERGE dbo.BusinessSubscriptions AS target
USING (SELECT @SubscriptionId AS BusinessSubscriptionId, @BusinessId AS BusinessId, @PlanId AS SubscriptionPlanId) AS source
ON target.BusinessSubscriptionId = source.BusinessSubscriptionId
WHEN MATCHED THEN UPDATE SET SubscriptionPlanId = source.SubscriptionPlanId, Status = 1,
    CurrentPeriodStart = DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1),
    CurrentPeriodEnd = DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)),
    PlanCodeSnapshot = N'essential', PlanNameSnapshot = N'Esencial', MonthlyPriceCop = 389999,
    IncludedCredits = 15000, MaxVariableCostCop = 100000, MaxVariableCostPercent = 25.64,
    ExtraCredits = 0, ExtraVariableCostCop = 0, AutoRenew = 1, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (BusinessSubscriptionId, BusinessId, SubscriptionPlanId, Status, CurrentPeriodStart, CurrentPeriodEnd,
     PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits, MaxVariableCostCop,
     MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop, AutoRenew, CreatedAt, UpdatedAt)
VALUES (@SubscriptionId, @BusinessId, @PlanId, 1,
    DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1),
    DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)),
    N'essential', N'Esencial', 389999, 15000, 100000, 25.64, 0, 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

UPDATE dbo.BusinessSubscriptions SET Status = 4, UpdatedAt = SYSUTCDATETIME()
WHERE BusinessId = @BusinessId AND BusinessSubscriptionId <> @SubscriptionId AND Status IN (1, 2, 3);

IF NOT EXISTS (SELECT 1 FROM dbo.BusinessUsagePeriods WHERE BusinessSubscriptionId = @SubscriptionId
    AND PeriodStart = DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1))
BEGIN
    INSERT INTO dbo.BusinessUsagePeriods
        (BusinessSubscriptionId, BusinessId, PeriodStart, PeriodEnd, CreditsIncluded, CreditsExtra,
         CreditsUsed, VariableCostLimitCop, VariableCostExtraCop, VariableCostUsedCop, Status, CreatedAt, UpdatedAt)
    VALUES (@SubscriptionId, @BusinessId, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1),
        DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)),
        15000, 0, 0, 100000, 0, 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END

IF (SELECT COUNT(*) FROM dbo.BusinessSubscriptions WHERE BusinessId = @BusinessId AND Status IN (1, 2, 3)) <> 1
    THROW 51000, 'SeedAndinaSantander: debe existir exactamente una suscripcion activa.', 1;

-- Brand vocabulary belongs to catalog data, not to cart/planner rules.
-- Kellogg's Colombia lists these as Kellogg's brands. The alias is intentionally
-- SuggestOnly because one brand query can match several active SKUs.
INSERT INTO dbo.ProductAliases
    (ProductAliasId, BusinessId, ProductId, Scope, CustomerKey, Alias, NormalizedAlias,
     Kind, ResolutionMode, Source, Status, UsageCount, CreatedAt)
SELECT NEWID(), @BusinessId, product.ProductId, 0, N'', N'Kellogg''s', N'kellogg',
       1, 0, 1, 1, 0, GETUTCDATE()
FROM dbo.Products product
WHERE product.BusinessId = @BusinessId
  AND product.IntegrationConnectionId = @XionCommerceConnectionId
  AND product.IsActive = 1
  AND (
      product.[Name] LIKE N'%ZUCARITAS%'
      OR product.[Name] LIKE N'%CHOCOKRISPIS%'
      OR product.[Name] LIKE N'%CHOCO KRISPIS%'
      OR product.[Name] LIKE N'%FROOT LOOPS%'
      OR product.[Name] LIKE N'%CORN FLAKES%'
      OR product.[Name] LIKE N'%ALL BRAN%'
      OR product.[Name] LIKE N'%RICE KRISPIES%'
      OR product.[Name] LIKE N'%SPECIAL K%'
  )
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.ProductAliases alias
      WHERE alias.BusinessId = @BusinessId
        AND alias.ProductId = product.ProductId
        AND alias.Scope = 0
        AND alias.CustomerKey = N''
        AND alias.NormalizedAlias = N'kellogg'
  );

PRINT N'SeedAndinaSantander: negocio, Xion y agente configurados.';

GO
