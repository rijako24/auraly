-- =============================================================================
-- SeedCJDistribuciones.sql
--
-- Negocio CJ Distribuciones con flujo de pedidos abierto, perfil comercial,
-- recomendaciones controladas por catalogo y cierre de pedido.
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @TenantId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000001';
DECLARE @BusinessId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000010';
DECLARE @AgentId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000020';
DECLARE @MantisCommerceConnectionId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000030';
DECLARE @AgentTypeId UNIQUEIDENTIFIER;

SELECT TOP (1) @AgentTypeId = AgentTypeId
FROM dbo.AgentTypes
WHERE IsActive = 1
ORDER BY Name;

IF @AgentTypeId IS NULL
BEGIN
    PRINT N'SeedCJDistribuciones: AgentType activo no encontrado; omitiendo.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.Tenants (TenantId, [Name], Email, IsActive, CreatedAt)
    VALUES (@TenantId, N'CJ Distribuciones', N'admin@cjdistribuciones.com', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Tenants
    SET [Name] = N'CJ Distribuciones',
        Email = N'admin@cjdistribuciones.com',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE TenantId = @TenantId;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    INSERT INTO dbo.Businesses
        (BusinessId, TenantId, [Name], [Description], [Address], Phone, Email, Website, TimeZone, IsActive, CreatedAt)
    VALUES
        (@BusinessId, @TenantId, N'CJ Distribuciones',
         N'Distribuidora de alimentos y productos de consumo para hogares, tiendas y distribuidores.',
         N'Valledupar, Cesar', N'+573000000000', N'admin@cjdistribuciones.com', N'', N'America/Bogota', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Businesses
    SET TenantId = @TenantId,
        [Name] = N'CJ Distribuciones',
        [Description] = N'Distribuidora de alimentos y productos de consumo para hogares, tiendas y distribuidores.',
        [Address] = COALESCE(NULLIF([Address], N''), N'Valledupar, Cesar'),
        Phone = COALESCE(NULLIF(Phone, N''), N'+573000000000'),
        Email = N'admin@cjdistribuciones.com',
        Website = COALESCE(Website, N''),
        TimeZone = N'America/Bogota',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId;
END

MERGE dbo.IntegrationConnections AS target
USING (
    SELECT
        @MantisCommerceConnectionId AS IntegrationConnectionId,
        @BusinessId AS BusinessId,
        CAST(1 AS INT) AS ConnectionType,
        CAST(3 AS INT) AS Provider,
        CAST(0 AS INT) AS Capability,
        N'Mantis Commerce CJ Distribuciones' AS [Name],
        N'Mantis' AS AccountIdentifier,
        N'{"baseUrl":"http://93.189.95.109:8080/MantisFiccCasalinsPruWeb/rest/","currency":"COP","requestTimeoutSeconds":30,"catalog":{"searchEndpoint":"pwsConsultarArticuloCasalins","defaultPageSize":5,"maxPageSize":5,"cacheProducts":true},"order":{"createEndpoint":"pwsCrearPedidoCasalins","warehouse":"1","mockCreateOrders":true}}' AS SettingsJson,
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

DECLARE @Products TABLE
(
    ProductId UNIQUEIDENTIFIER NOT NULL,
    Sku NVARCHAR(100) NULL,
    [Name] NVARCHAR(250) NOT NULL,
    [Description] NVARCHAR(MAX) NULL,
    CategoryName NVARCHAR(150) NULL,
    UnitPrice DECIMAL(18, 2) NOT NULL,
    Currency NVARCHAR(10) NOT NULL,
    StockQuantity DECIMAL(18, 2) NULL,
    IsActive BIT NOT NULL
);

INSERT INTO @Products
    (ProductId, Sku, [Name], [Description], CategoryName, UnitPrice, Currency, StockQuantity, IsActive)
VALUES
    ('C1D15A00-0000-0000-0000-000000000100', N'CJ-PECHUGA-KG', N'Pechuga de pollo x kg', N'Pechuga de pollo fresca para preparaciones de hogar, tienda o negocio.', N'Carnes y pollo', 18500, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000101', N'CJ-POLLO-ENTERO-KG', N'Pollo entero x kg', N'Pollo entero fresco por kilogramo.', N'Carnes y pollo', 9800, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000102', N'CJ-CARNE-MOLIDA-KG', N'Carne molida x kg', N'Carne molida para comidas rapidas, almuerzos y preparaciones familiares.', N'Carnes y pollo', 22000, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000103', N'CJ-ARROZ-5KG', N'Arroz premium 5 kg', N'Arroz blanco premium en presentacion familiar de 5 kg.', N'Granos y abarrotes', 24500, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000104', N'CJ-PASTA-500G', N'Pasta espagueti 500 g', N'Pasta tipo espagueti para preparaciones caseras.', N'Granos y abarrotes', 4200, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000105', N'CJ-QUESO-COSTENO-KG', N'Queso costeno x kg', N'Queso costeno para hogar, tienda y preparaciones tradicionales.', N'Lacteos', 26500, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000106', N'CJ-ACEITE-3000ML', N'Aceite vegetal 3000 ml', N'Aceite vegetal para cocina en presentacion familiar.', N'Abarrotes', 29500, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000107', N'CJ-SALSA-TOMATE-1000G', N'Salsa de tomate 1000 g', N'Salsa de tomate para cocina, negocios y comidas rapidas.', N'Salsas', 9800, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000108', N'CJ-PAPA-FRANCESA-2-5KG', N'Papa a la francesa 2.5 kg', N'Papa congelada para freir en presentacion de 2.5 kg.', N'Congelados', 21800, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000109', N'CJ-QUESILLO-20-LONCHAS', N'Quesillo 20 lonchas', N'Quesillo tajado en paquete de 20 lonchas para comidas rapidas y preparaciones.', N'Lacteos', 14200, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000110', N'CJ-JAMON-AHUMADO-500G', N'Jamon ahumado 500 g', N'Jamon ahumado tajado para sandwiches, rollitos y comidas rapidas.', N'Carnes frias', 16800, N'COP', NULL, 1),
    ('C1D15A00-0000-0000-0000-000000000111', N'CJ-TOCINETA-500G', N'Tocineta ahumada 500 g', N'Tocineta ahumada para hamburguesas, rollitos, perros y acompanamientos.', N'Carnes frias', 23500, N'COP', NULL, 1);

MERGE dbo.Products AS target
USING @Products AS source
   ON target.BusinessId = @BusinessId
  AND target.Sku = source.Sku
WHEN MATCHED THEN
    UPDATE SET
        IntegrationConnectionId = @MantisCommerceConnectionId,
        ExternalProductId = NULL,
        Source = 0,
        [Name] = source.[Name],
        [Description] = source.[Description],
        CategoryName = source.CategoryName,
        UnitPrice = source.UnitPrice,
        Currency = source.Currency,
        ManageStock = 0,
        StockQuantity = source.StockQuantity,
        IsActive = source.IsActive,
        RawPayloadJson = NULL,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (ProductId, BusinessId, IntegrationConnectionId, ExternalProductId, Source, Sku, [Name],
            [Description], CategoryName, UnitPrice, Currency, ManageStock, StockQuantity,
            IsActive, RawPayloadJson, LastSyncedAt, CreatedAt)
    VALUES (source.ProductId, @BusinessId, @MantisCommerceConnectionId, NULL, 0, source.Sku, source.[Name],
            source.[Description], source.CategoryName, source.UnitPrice, source.Currency, 0, source.StockQuantity,
            source.IsActive, NULL, NULL, GETUTCDATE());

DELETE FROM dbo.Products
WHERE BusinessId = @BusinessId
  AND Source = 0
  AND NOT EXISTS (
      SELECT 1
      FROM @Products p
      WHERE p.Sku = dbo.Products.Sku
  );

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
    "model":  "gpt-4.1-mini",
    "temperature":  0.4,
    "historyWindowSize":  24,
    "commerce":  {
                     "enabled":  true,
                     "provider":  "Mantis"
                 },
    "operatingHours":  {
                           "enforce":  false,
                           "outsideHours":  {
                                                "guidance":  "Responde de forma breve, cordial y cerrada. Explica que el negocio esta fuera de horario y que el proximo horario habil es {{next_operating_window}}. Adapta el mensaje a lo que dijo el cliente, pero no solicites datos, no prometas ejecutar gestiones, no abras catalogos y no termines con preguntas."
                                            }
                       },
    "persona":  "Eres el asistente comercial de CJ Distribuciones por WhatsApp. Atiendes pedidos de alimentos y productos de consumo para hogares y negocios. Hablas en espanol con tono claro, amable, breve y practico. El saludo inicial y el cierre son los momentos para usar el nombre del cliente; en los turnos intermedios respondes directamente. El catalogo y los resultados de las operaciones son la fuente de verdad comercial.",
    "policies":  "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\n## PRESENTACION\n\n- Presentate como asistente de CJ Distribuciones con tono breve, amable y practico.\n- Reserva el nombre del cliente para el saludo inicial y el cierre; en los turnos intermedios responde directamente.\n- Presenta catalogos, precios, carrito, totales y estado del pedido exclusivamente desde resultados oficiales del turno.",
    "messageSequences":  {
                             "order_created_customer":  {
                                                            "messages":  [
                                                                             {
                                                                                 "body":  "Gracias por tu pedido, {customer_name}. Lo recibimos correctamente y ya estamos coordinando la entrega."
                                                                             }
                                                                         ]
                                                        }
                         },
    "globalActions":  [
                          {
                              "id":  "human_handoff",
                              "priority":  1000,
                              "goal":  "Escalar a humano cuando el cliente lo pida, haya queja, caso mayorista especial o solicitud fuera del alcance.",
                              "conversationGuidance":  "Detecta ?nicamente una solicitud expl?cita de atenci?n humana, una queja que requiera intervenci?n o una negociaci?n especial fuera del alcance configurado.",
                              "signal":  {
                                             "type":  "human_escalation",
                                             "description":  "Solicitud expl?cita de hablar con una persona, queja que requiere intervenci?n o negociaci?n comercial especial fuera del alcance.",
                                             "valueSchema":  {
                                                                 "type":  "boolean"
                                                             }
                                         },
                              "actions":  [
                                              {
                                                  "id":  "request_human",
                                                  "operation":  "escalation.request_human",
                                                  "trigger":  "on_signal",
                                                  "signal":  "human_escalation",
                                                  "arguments":  {
                                                                    "reason":  "{{turn.message}}",
                                                                    "last_user_message":  "{{turn.message}}"
                                                                },
                                                  "onOutcome":  {
                                                                    "escalation.requested":  {
                                                                                                 "effects":  [
                                                                                                                 {
                                                                                                                     "type":  "escalation.human",
                                                                                                                     "reason":  "customer_request"
                                                                                                                 }
                                                                                                             ],
                                                                                                 "response":  {
                                                                                                                  "mode":  "deterministic",
                                                                                                                  "guidance":  "Informa brevemente que ser? atendido por una persona."
                                                                                                              }
                                                                                             },
                                                                    "escalation.notification_failed":  {
                                                                                                           "response":  {
                                                                                                                            "mode":  "deterministic",
                                                                                                                            "guidance":  "Informa que registrar?s la solicitud para atenci?n humana sin prometer un tiempo exacto."
                                                                                                                        }
                                                                                                       }
                                                                }
                                              }
                                          ]
                          }
                      ],
    "factSchema":  [
                       {
                           "key":  "customer_name",
                           "role":  "customer.name",
                           "label":  "nombre del cliente o establecimiento",
                           "type":  "string",
                           "required":  true,
                           "source":  "user",
                           "scope":  "customer"
                       },
                       {
                           "key":  "customer_type",
                           "role":  "customer.type",
                           "label":  "perfil del cliente",
                           "type":  "string",
                           "required":  true,
                           "source":  "user",
                           "scope":  "customer",
                           "options":  [
                                           {
                                               "label":  "Hogar",
                                               "selector":  "A",
                                               "value":  "Hogar"
                                           },
                                           {
                                               "label":  "Tienda o minimercado",
                                               "selector":  "B",
                                               "value":  "TiendaMinimercado"
                                           },
                                           {
                                               "label":  "Restaurante",
                                               "selector":  "C",
                                               "value":  "Restaurante"
                                           },
                                           {
                                               "label":  "Comida rapida",
                                               "selector":  "D",
                                               "value":  "ComidaRapida"
                                           },
                                           {
                                               "label":  "Distribuidor",
                                               "selector":  "E",
                                               "value":  "Distribuidor"
                                           }
                                       ]
                       },
                       {
                           "key":  "order_finalized",
                           "role":  "order.finalized",
                           "label":  "cliente finalizo el carrito",
                           "type":  "boolean",
                           "required":  true,
                           "source":  "user",
                           "scope":  "request",
                           "retentionDays":  1,
                           "extractionGuidance":  "Representa que el cliente comunico que termino la seleccion de productos y desea continuar con el pedido."
                       },
                       {
                           "key":  "cart_review_confirmed",
                           "role":  "order.cart_review_confirmed",
                           "label":  "carrito aprobado por el cliente",
                           "type":  "boolean",
                           "required":  true,
                           "source":  "user",
                           "scope":  "request",
                           "retentionDays":  1,
                           "extractionGuidance":  "Representa la aprobacion explicita del resumen vigente del carrito."
                       },
                       {
                           "key":  "delivery_method",
                           "role":  "shipping.method",
                           "label":  "modalidad de entrega",
                           "type":  "string",
                           "required":  true,
                           "source":  "user",
                           "scope":  "request",
                           "retentionDays":  1,
                           "extractionGuidance":  "Normaliza la modalidad elegida al valor canonico configurado para entrega o recogida.",
                           "options":  [
                                           {
                                               "value":  "domicilio",
                                               "label":  "Domicilio"
                                           },
                                           {
                                               "value":  "recogida",
                                               "label":  "Recogida"
                                           }
                                       ]
                       },
                       {
                           "key":  "city",
                           "role":  "shipping.city",
                           "label":  "ciudad de entrega",
                           "type":  "string",
                           "required":  true,
                           "source":  "system",
                           "defaultValue":  "Valledupar",
                           "scope":  "request",
                           "retentionDays":  1
                       },
                       {
                           "key":  "delivery_address",
                           "role":  "shipping.address",
                           "label":  "direccion de entrega o recogida",
                           "type":  "string",
                           "required":  true,
                           "source":  "user",
                           "scope":  "request",
                           "retentionDays":  1,
                           "extractionGuidance":  "Extrae solo la ubicacion fisica. Si el mismo mensaje incluye telefono o celular, excluye de la direccion el numero telefonico y expresiones de enlace como y el telefono es, y el numero es o variantes con errores ortograficos."
                       },
                       {
                           "key":  "delivery_phone",
                           "role":  "customer.phone",
                           "label":  "celular de entrega",
                           "type":  "phone",
                           "required":  true,
                           "source":  "user",
                           "scope":  "customer"
                       },
                       {
                           "key":  "payment_method",
                           "role":  "payment.method",
                           "label":  "metodo de pago",
                           "type":  "string",
                           "required":  true,
                           "source":  "user",
                           "scope":  "request",
                           "retentionDays":  1,
                           "extractionGuidance":  "Normaliza la eleccion al metodo de pago canonico configurado.",
                           "options":  [
                                           {
                                               "value":  "efectivo",
                                               "label":  "Efectivo"
                                           },
                                           {
                                               "value":  "transferencia",
                                               "label":  "Transferencia"
                                           }
                                       ]
                       },
                       {
                           "key":  "order_checkout_presented",
                           "role":  "order.checkout_presented",
                           "label":  "resumen final presentado",
                           "type":  "boolean",
                           "required":  false,
                           "source":  "system",
                           "scope":  "request",
                           "retentionDays":  1
                       },
                       {
                           "key":  "system.recipe_catalog_queries",
                           "role":  "system.recipe_catalog_queries",
                           "label":  "consultas de catalogo derivadas de receta",
                           "type":  "json",
                           "required":  false,
                           "source":  "system",
                           "scope":  "request",
                           "retentionDays":  1
                       },
                       {
                           "key":  "customer_confirmed",
                           "role":  "confirmation.verbal",
                           "label":  "confirmacion verbal del pedido",
                           "type":  "boolean",
                           "required":  false,
                           "source":  "user",
                           "scope":  "request",
                           "dependsOn":  [
                                             "order_checkout_presented",
                                             "cart_review_confirmed",
                                             "delivery_method",
                                             "city",
                                             "delivery_address",
                                             "delivery_phone",
                                             "customer_name",
                                             "payment_method"
                                         ],
                           "retentionDays":  1,
                           "extractionGuidance":  "Representa la confirmacion explicita del resumen final vigente."
                       }
                   ],
    "notifications":  {
                          "order_created":  {
                                                "enabled":  false,
                                                "recipients":  [

                                                               ],
                                                "sendMessageSequence":  null
                                            }
                      },
    "webhooks":  {

                 },
    "escalations":  {
                        "human":  {
                                      "contacts":  [

                                                   ]
                                  },
                        "external":  {
                                         "enabled":  false,
                                         "events":  {

                                                    }
                                     }
                    },
    "checkout":  {
                     "currency":  "COP",
                     "modes":  {
                                   "order":  {
                                                 "paymentMethods":  {
                                                                        "efectivo":  {
                                                                                         "label":  "efectivo al recibir",
                                                                                         "aliases":  [
                                                                                                         "efectivo",
                                                                                                         "contraentrega"
                                                                                                     ],
                                                                                         "template":  "order_checkout_no_payment"
                                                                                     },
                                                                        "transferencia":  {
                                                                                              "label":  "transferencia manual",
                                                                                              "aliases":  [
                                                                                                              "transferencia",
                                                                                                              "nequi",
                                                                                                              "bancolombia"
                                                                                                          ],
                                                                                              "template":  "order_checkout_manual_transfer",
                                                                                              "manualConfirmationRequired":  true,
                                                                                              "manualExpirationMinutes":  1440,
                                                                                              "confirmationOutcome":  "order_paid"
                                                                                          }
                                                                    },
                                                 "shipping":  {
                                                                  "enabled":  true,
                                                                  "localCity":  "Valledupar",
                                                                  "localCost":  6000,
                                                                  "nationalCost":  25000
                                                              }
                                             }
                               }
                 },
    "templates":  {
                      "order_checkout_no_payment":  "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nMetodo de pago: efectivo al recibir\n\nConfirmas tu pedido con esta informacion?",
                      "order_checkout_manual_transfer":  "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nMetodo de pago: transferencia manual\n\nTu pago queda pendiente de confirmacion manual. Un agente del equipo de CJ Distribuciones confirmara el pago; cuando se confirme, te notificaremos que el pedido fue creado.",
                      "catalog_results":  "Estas son las opciones que encontre para ti:\r\n\r\n*Productos disponibles*\r\n{{#each products}}\r\n- {{name}}: ${{unit_price}} {{currency}}\r\n{{/each}}\r\n\r\nCuales te gustaria agregar y en que cantidad?",
                      "catalog_no_results":  "Por ahora no encontre {{#if search_text}}{{search_text}}{{else}}productos para esa busqueda{{/if}} disponibles en nuestro catalogo. Si quieres, puedo buscar opciones parecidas o ayudarte a elegir otro producto.",
                      "recipe_results":  "Buena idea. Puedes inspirarte con estas preparaciones:\r\n\r\n*Ideas para preparar*\r\n{{#each results}}\r\n- {{Title}}\r\n  {{Url}}\r\n{{/each}}",
                      "cart_snapshot":  "Listo, asi va tu pedido:\r\n\r\n*Pedido actual*\r\n{{#each items}}\r\n- {{name}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n*Total: ${{total}} {{currency}}*\r\n\r\nPuedes agregar algo mas o decirme cuando este completo.",
                      "cart_review":  "Perfecto, revisemos juntos el pedido:\r\n\r\n*Resumen de tu pedido*\r\n{{#each items}}\r\n- {{name}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n*Total: ${{total}} {{currency}}*\r\n\r\nEsta correcto o deseas ajustar algo?",
                      "product_ambiguity":  "Quiero asegurarme de agregar la opcion correcta. Para {{product_text}} encontre:\r\n{{#each product_options}}\r\n- {{Name}}: ${{UnitPrice}} {{Currency}}\r\n{{/each}}\r\n\r\nCual prefieres? Conservare los demas productos de tu solicitud.",
                      "insufficient_stock":  "Puedo ayudarte con esa referencia, pero la cantidad solicitada supera el inventario disponible.\r\n\r\n- Producto: {{product_text}}\r\n- Solicitado en total: {{requested_quantity}}\r\n- Disponible: {{available_quantity}}\r\n\r\nPara este cambio, indica una cantidad de hasta {{maximum_command_quantity}}; los demas cambios del lote aun no se han aplicado.",
                      "product_selection_prompt":  "Con gusto te ayudo a elegir. Dime que productos y cantidades necesitas, o que deseas preparar para recomendarte opciones.",
                      "order_draft_unavailable":  "No fue posible consultar el pedido vigente en este momento. Intenta nuevamente para continuar con el resumen.",
                      "customer_name_prompt":  "Hola! Bienvenido a CJ Distribuciones. Con gusto te ayudo a realizar tu pedido. Me indicas tu nombre o el nombre de tu establecimiento?",
                      "customer_type_prompt":  "Mucho gusto, {{customer_name}}. Selecciona el perfil que describe tu compra:\r\nA. Hogar\r\nB. Tienda o minimercado\r\nC. Restaurante\r\nD. Comida rapida\r\nE. Distribuidor"
                  },
    "flows":  [
                  {
                      "id":  "order",
                      "type":  "primary",
                      "routingGuidance":  "Use this primary flow for CJ Distribuciones product orders, customer identification, profile classification, catalog-grounded recommendations, delivery data, payment method and order confirmation.",
                      "stages":  [
                                     {
                                         "id":  "customer_name",
                                         "name":  "Identificacion del cliente",
                                         "goal":  "Obtener el nombre del cliente o establecimiento antes de iniciar el pedido cuando no exista un nombre confiable.",
                                         "advanceWhenFacts":  [
                                                                  "customer_name"
                                                              ],
                                         "conversationGuidance":  "Si falta customer_name y el cliente no lo informo en el mensaje actual, saluda exactamente: Hola! Bienvenido a CJ Distribuciones. Con gusto te ayudo a realizar tu pedido. Me indicas tu nombre o el nombre de tu establecimiento? Si ya lo dijo, continÃºa sin volver a pedirlo; el motor registra el dato extraÃ­do.",
                                         "collect":  [
                                                         "customer_name",
                                                         "customer_type"
                                                     ],
                                         "response":  {
                                                          "fallbackTemplate":  "customer_name_prompt",
                                                          "clarificationTemplate":  "customer_name_prompt"
                                                      }
                                     },
                                     {
                                         "id":  "customer_type",
                                         "name":  "Perfil del cliente",
                                         "goal":  "Clasificar el perfil comercial como Hogar, TiendaMinimercado, Restaurante, ComidaRapida o Distribuidor.",
                                         "advanceWhenFacts":  [
                                                                  "customer_type"
                                                              ],
                                         "conversationGuidance":  "Si falta customer_type, pregunta: Mucho gusto, {customer_name}. Para brindarte informacion y recomendaciones mas adecuadas, cual de estas opciones describe mejor tu perfil? A. Hogar B. Tienda o minimercado C. Restaurante D. Comida rapida E. Distribuidor. Acepta respuestas naturales y registra el valor canonico. Si el cliente corrige el perfil posteriormente, actualizalo.",
                                         "collect":  [
                                                         "customer_type"
                                                     ],
                                         "response":  {
                                                          "fallbackTemplate":  "customer_type_prompt",
                                                          "clarificationTemplate":  "customer_type_prompt"
                                                      }
                                     },
                                     {
                                         "id":  "product_selection",
                                         "name":  "Productos, catalogo y recomendaciones",
                                         "goal":  "Recibir pedidos abiertos, resolver productos reales del catalogo, recomendar de forma controlada y construir el carrito hasta que el cliente finalice.",
                                         "advanceWhenFacts":  [
                                                                  "order_finalized"
                                                              ],
                                         "conversationGuidance":  "Acompana al cliente de forma cercana mientras elige productos. Las consultas comerciales se presentan con resultados autoritativos del catalogo. Las solicitudes de preparacion producen ideas de receta y productos relacionados en el mismo turno. Cuando solicite productos y cantidades, conserva el lote completo para que el motor lo aplique al unico pedido activo. Tras cada cambio presenta el estado vigente con una transicion natural. Cuando el cliente comunique que termino la seleccion, registra order_finalized=true.",
                                         "collect":  [
                                                         "order_finalized",
                                                         "delivery_method",
                                                         "delivery_address",
                                                         "delivery_phone",
                                                         "payment_method"
                                                     ],
                                         "signals":  [
                                                         {
                                                             "type":  "recipe_request",
                                                             "description":  "Solicitud de ideas para preparar una comida. El valor contiene el ingrediente o la preparacion principal que debe buscarse.",
                                                             "valueSchema":  {
                                                                                 "type":  "string"
                                                                             }
                                                         },
                                                         {
                                                             "type":  "order_changes",
                                                             "description":  "Una mutacion explicita de uno o varios productos del unico pedido activo. Representa cada producto afectado con exactamente un comando: add cuando la cantidad es incremental o corresponde a un producto nuevo; set_quantity cuando la cantidad expresa el total final deseado para una linea existente; remove cuando se elimina por completo la linea, con quantity nulo. Cada comando corresponde a un producto afectado en el mensaje actual y se emite exactamente una vez. Conserva referencias parciales o contextuales, todas las cantidades y todos los productos del turno; el historial se usa para resolver la referencia, mientras las mutaciones provienen del mensaje actual. El motor resuelve catalogo, ambiguedad e inventario de forma autoritativa. Cuando exista una seleccion pendiente, la referencia elegida continua esa misma mutacion y el motor restaura el resto del lote.",
                                                             "valueSchema":  {
                                                                                 "type":  "array",
                                                                                 "items":  {"anyOf":[{"type":"object","additionalProperties":false,"properties":{"operation":{"type":"string","enum":["add"]},"productText":{"type":"string"},"quantity":{"type":"number"},"destinationReference":{"type":["string","null"]}},"required":["operation","productText","quantity","destinationReference"]},{"type":"object","additionalProperties":false,"properties":{"operation":{"type":"string","enum":["set_quantity"]},"productText":{"type":"string"},"quantity":{"type":"number"},"destinationReference":{"type":["string","null"]}},"required":["operation","productText","quantity","destinationReference"]},{"type":"object","additionalProperties":false,"properties":{"operation":{"type":"string","enum":["remove"]},"productText":{"type":"string"},"quantity":{"type":"null"},"destinationReference":{"type":["string","null"]}},"required":["operation","productText","quantity","destinationReference"]}]}
                                                                             },
                                                             "ambiguityRules":  [
                                                                                    {
                                                                                        "type":  "distinct_values",
                                                                                        "valueProperty":  "destinationReference",
                                                                                        "field":  "delivery_address",
                                                                                        "minimumDistinctValues":  2
                                                                                    }
                                                                                ]
                                                         },
                                                         {
                                                             "type":  "catalog_query",
                                                             "description":  "Consulta de existencia, opciones, referencias, precios, disponibilidad o recomendaciones sin una instruccion explicita de agregar cantidades al pedido. El valor contiene terminos de busqueda concretos resueltos desde el turno y su contexto conversacional.",
                                                             "valueSchema":  {
                                                                                 "type":  "object",
                                                                                 "additionalProperties":  false,
                                                                                 "properties":  {
                                                                                                    "queries":  {
                                                                                                                    "type":  "array",
                                                                                                                    "items":  {
                                                                                                                                  "type":  "string"
                                                                                                                              },
                                                                                                                    "minItems":  1
                                                                                                                }
                                                                                                },
                                                                                 "required":  [
                                                                                                  "queries"
                                                                                              ]
                                                                             }
                                                         }
                                                     ],
                                         "actions":  [
                                                         {
                                                             "id":  "search_recipe_request",
                                                             "operation":  "commerce.search_recipes",
                                                             "execution":  { "idempotency": "none" },
                                                             "trigger":  "on_signal",
                                                             "signal":  "recipe_request",
                                                             "arguments":  {
                                                                               "ingredient":  "{{signal.recipe_request.value}}",
                                                                               "query":  "preparacion facil",
                                                                               "limit":  2
                                                                           },
                                                             "onOutcome":  {
                                                                               "recipes.found":  {
                                                                                                     "effects":  [
                                                                                                                     {
                                                                                                                         "type":  "facts.set_from_outcome",
                                                                                                                         "bindings":  {
                                                                                                                                          "system.recipe_catalog_queries":  "catalog_search_queries"
                                                                                                                                      }
                                                                                                                     },
                                                                                                                     {
                                                                                                                         "type":  "presentation.add",
                                                                                                                         "template":  "recipe_results",
                                                                                                                         "mode":  "Exclusive",
                                                                                                                         "priority":  "Required"
                                                                                                                     }
                                                                                                                 ],
                                                                                                     "response":  {
                                                                                                                      "guidance":  "Presenta mÃ¡ximo dos ideas devueltas y luego muestra Ãºnicamente ingredientes encontrados en el catÃ¡logo oficial."
                                                                                                                  }
                                                                                                 }
                                                                           }
                                                         },
                                                         {
                                                             "id":  "search_recipe_catalog_products",
                                                             "operation":  "commerce.search_products",
                                                             "execution":  { "idempotency": "none" },
                                                             "trigger":  "when_ready",
                                                             "condition":  {
                                                                               "factPresent":  "system.recipe_catalog_queries"
                                                                           },
                                                             "arguments":  {
                                                                               "queries":  "{{fact.system.recipe_catalog_queries}}",
                                                                               "limit":  10
                                                                           },
                                                             "onOutcome":  {
                                                                               "products.not_found":  {
                                                                                                          "effects":  [
                                                                                                                          {
                                                                                                                              "type":  "facts.clear",
                                                                                                                              "facts":  ["system.recipe_catalog_queries"]
                                                                                                                          },
                                                                                                                          {
                                                                                                                              "type":  "presentation.add",
                                                                                                                              "template":  "catalog_no_results",
                                                                                                                              "mode":  "Exclusive",
                                                                                                                              "priority":  "Required"
                                                                                                                          }
                                                                                                                      ]
                                                                                                      },
                                                                               "products.found":  {
                                                                                                      "effects":  [
                                                                                                                      {
                                                                                                                          "type":  "facts.clear",
                                                                                                                          "facts":  [
                                                                                                                                        "system.recipe_catalog_queries"
                                                                                                                                    ]
                                                                                                                      },
                                                                                                                      {
                                                                                                                          "type":  "presentation.add",
                                                                                                                          "template":  "catalog_results",
                                                                                                                          "mode":  "Exclusive",
                                                                                                                          "priority":  "Required"
                                                                                                                      }
                                                                                                                  ],
                                                                                                      "response":  {
                                                                                                                       "guidance":  "Muestra solo productos reales devueltos por catÃ¡logo, con presentaciÃ³n y precio cuando estÃ©n disponibles."
                                                                                                                   }
                                                                                                  }
                                                                           }
                                                         },
                                                         {
                                                             "id":  "apply_order_changes",
                                                             "operation":  "commerce.apply_order_changes",
                                                             "trigger":  "on_signal",
                                                             "signal":  "order_changes",
                                                             "arguments":  {
                                                                               "commands":  "{{signal.order_changes.value}}"
                                                                           },
                                                             "onOutcome":  {
                                                                               "cart.applied":  {
                                                                                                    "response":  {
                                                                                                                     "guidance":  "Confirma brevemente los cambios aplicados y continÃºa segÃºn el objetivo de la etapa."
                                                                                                                 },
                                                                                                    "effects":  [
                                                                                                                    {
                                                                                                                        "type":  "presentation.add",
                                                                                                                        "template":  "cart_snapshot",
                                                                                                                        "dataPath":  "order",
                                                                                                                        "mode":  "Exclusive",
                                                                                                                        "priority":  "Required"
                                                                                                                    }
                                                                                                                ]
                                                                                                },
                                                                               "cart.product_not_found":  {
                                                                                                              "response":  {
                                                                                                                               "mode":  "ask_clarification",
                                                                                                                               "guidance":  "Indica que ese producto no se encontrÃ³ y pide una descripciÃ³n o referencia mÃ¡s precisa."
                                                                                                                           }
                                                                                                          },
                                                                               "cart.product_ambiguous":  {
                                                                                                              "response":  {
                                                                                                                               "mode":  "ask_clarification",
                                                                                                                               "guidance":  "Presenta Ãºnicamente los candidatos devueltos y pregunta cuÃ¡l referencia desea."
                                                                                                                           },
                                                                                                              "effects":  [
                                                                                                                              {
                                                                                                                                  "type": "presentation.add",
                                                                                                                                  "template": "product_ambiguity",
                                                                                                                                  "dataPath": "error.context",
                                                                                                                                  "mode": "Exclusive",
                                                                                                                                  "priority": "Required"
                                                                                                                              }
                                                                                                                          ]
                                                                                                          },
                                                                               "cart.insufficient_stock":  {
                                                                                                              "response":  {
                                                                                                                               "mode":  "ask_clarification",
                                                                                                                               "guidance":  "Explica con claridad la cantidad disponible y pide una cantidad valida; ningun cambio del lote fue aplicado."
                                                                                                                           },
                                                                                                              "effects":  [
                                                                                                                              {
                                                                                                                                  "type": "presentation.add",
                                                                                                                                  "template": "insufficient_stock",
                                                                                                                                  "dataPath": "error.context",
                                                                                                                                  "mode": "Exclusive",
                                                                                                                                  "priority": "Required"
                                                                                                                              }
                                                                                                                          ]
                                                                                                          },                                                                               "cart.item_not_found_or_ambiguous":  {
                                                                                                                        "response":  {
                                                                                                                                         "mode":  "ask_clarification",
                                                                                                                                         "guidance":  "Aclara cuÃ¡l producto existente del pedido desea modificar."
                                                                                                                                     }
                                                                                                                    },
                                                                               "cart.conflicting_commands":  {
                                                                                                                 "response":  {
                                                                                                                                  "mode":  "ask_clarification",
                                                                                                                                  "guidance":  "Pide aclarar el cambio final para el producto repetido; no se aplicÃ³ ningÃºn cambio del lote."
                                                                                                                              }
                                                                                                             },
                                                                               "cart.multiple_destinations":  {
                                                                                                                  "response":  {
                                                                                                                                   "mode":  "ask_clarification",
                                                                                                                                   "guidance":  "No se aplicÃ³ ningÃºn cambio. Pregunta cuÃ¡l direcciÃ³n debe usarse para entregar todo el Ãºnico pedido."
                                                                                                                               }
                                                                                                              }
                                                                           }
                                                         },
                                                         {
                                                             "id":  "search_catalog_request",
                                                             "operation":  "commerce.search_products",
                                                             "execution":  { "idempotency": "none" },
                                                             "trigger":  "on_signal",
                                                             "signal":  "catalog_query",
                                                             "arguments":  {
                                                                               "queries":  "{{signal.catalog_query.value.queries}}",
                                                                               "limit":  10
                                                                           },
                                                             "onOutcome":  {
                                                                               "products.not_found":  {
                                                                                                          "effects":  [
                                                                                                                          {
                                                                                                                              "type":  "presentation.add",
                                                                                                                              "template":  "catalog_no_results",
                                                                                                                              "mode":  "Exclusive",
                                                                                                                              "priority":  "Required"
                                                                                                                          }
                                                                                                                      ]
                                                                                                      },
                                                                               "products.found":  {
                                                                                                      "effects":  [
                                                                                                                      {
                                                                                                                          "type":  "presentation.add",
                                                                                                                          "template":  "catalog_results",
                                                                                                                          "mode":  "Exclusive",
                                                                                                                          "priority":  "Required"
                                                                                                                      }
                                                                                                                  ]
                                                                                                  }
                                                                           }
                                                         }
                                                     ],
                                         "response":  {
                                                          "fallbackTemplate":  "product_selection_prompt"
                                                      }
                                     },
                                     {
                                         "id":  "cart_review",
                                         "name":  "Transicion al cierre",
                                         "goal":  "Continuar hacia entrega y pago sin mostrar un resumen intermedio.",
                                         "advanceWhenFacts":  [
                                                                  "order_finalized"
                                                              ],
                                         "conversationGuidance":  "Cuando el cliente termine de agregar productos, avanza directamente a modalidad y datos de entrega. No muestres ni solicites confirmacion de un resumen intermedio; el unico resumen de cierre se presenta despues de completar entrega y pago.",
                                         "collect":  [
                                                         "order_finalized",
                                                         "delivery_method",
                                                         "delivery_address",
                                                         "delivery_phone",
                                                         "payment_method"
                                                     ],
                                         "signals":  [
                                                         {
                                                             "type":  "order_changes",
                                                             "description":  "Una mutacion explicita de uno o varios productos del unico pedido activo. Representa cada producto afectado con exactamente un comando: add cuando la cantidad es incremental o corresponde a un producto nuevo; set_quantity cuando la cantidad expresa el total final deseado para una linea existente; remove cuando se elimina por completo la linea, con quantity nulo. Cada comando corresponde a un producto afectado en el mensaje actual y se emite exactamente una vez. Conserva referencias parciales o contextuales, todas las cantidades y todos los productos del turno; el historial se usa para resolver la referencia, mientras las mutaciones provienen del mensaje actual. El motor resuelve catalogo, ambiguedad e inventario de forma autoritativa. Cuando exista una seleccion pendiente, la referencia elegida continua esa misma mutacion y el motor restaura el resto del lote.",
                                                             "valueSchema":  {
                                                                                 "type":  "array",
                                                                                 "items":  {"anyOf":[{"type":"object","additionalProperties":false,"properties":{"operation":{"type":"string","enum":["add"]},"productText":{"type":"string"},"quantity":{"type":"number"},"destinationReference":{"type":["string","null"]}},"required":["operation","productText","quantity","destinationReference"]},{"type":"object","additionalProperties":false,"properties":{"operation":{"type":"string","enum":["set_quantity"]},"productText":{"type":"string"},"quantity":{"type":"number"},"destinationReference":{"type":["string","null"]}},"required":["operation","productText","quantity","destinationReference"]},{"type":"object","additionalProperties":false,"properties":{"operation":{"type":"string","enum":["remove"]},"productText":{"type":"string"},"quantity":{"type":"null"},"destinationReference":{"type":["string","null"]}},"required":["operation","productText","quantity","destinationReference"]}]}
                                                                             },
                                                             "ambiguityRules":  [
                                                                                    {
                                                                                        "type":  "distinct_values",
                                                                                        "valueProperty":  "destinationReference",
                                                                                        "field":  "delivery_address",
                                                                                        "minimumDistinctValues":  2
                                                                                    }
                                                                                ]
                                                         }
                                                     ],
                                         "actions":  [
                                                         {
                                                             "id":  "show_current_order_draft",
                                                             "operation":  "commerce.get_order_draft",
                                                             "trigger":  "when_ready",
                                                             "condition":  {
                                                                               "factMissing":  "order_finalized"
                                                                           },
                                                             "arguments":  {

                                                                           },
                                                             "onOutcome":  {
                                                                               "order.draft_loaded":  {
                                                                                                          "response":  {
                                                                                                                           "guidance":  "Muestra los Ã­tems, cantidades, subtotales y total devueltos, y pregunta si el pedido actual estÃ¡ correcto."
                                                                                                                       },
                                                                                                          "effects":  [
                                                                                                                          {
                                                                                                                              "type":  "presentation.add",
                                                                                                                              "template":  "cart_review",
                                                                                                                              "dataPath":  "order",
                                                                                                                              "mode":  "Exclusive",
                                                                                                                              "priority":  "Required"
                                                                                                                          }
                                                                                                                      ]
                                                                                                      }
,
                                                                               "order.draft_empty":  {
                                                                                                           "response":  { "fallbackTemplate":  "product_selection_prompt" },
                                                                                                           "effects":  [
                                                                                                                           {
                                                                                                                               "type":  "facts.clear",
                                                                                                                               "facts":  [ "order_finalized", "cart_review_confirmed" ]
                                                                                                                           }
                                                                                                                       ]
                                                                                                       }                                                                           }
                                                         },
                                                         {
                                                             "id":  "apply_order_changes",
                                                             "operation":  "commerce.apply_order_changes",
                                                             "trigger":  "on_signal",
                                                             "signal":  "order_changes",
                                                             "arguments":  {
                                                                               "commands":  "{{signal.order_changes.value}}"
                                                                           },
                                                             "onOutcome":  {
                                                                               "cart.applied":  {
                                                                                                    "response":  {
                                                                                                                     "guidance":  "Confirma brevemente los cambios aplicados y continÃºa segÃºn el objetivo de la etapa."
                                                                                                                 },
                                                                                                    "effects":  [
                                                                                                                    {
                                                                                                                        "type":  "presentation.add",
                                                                                                                        "template":  "cart_review",
                                                                                                                        "dataPath":  "order",
                                                                                                                        "mode":  "Exclusive",
                                                                                                                        "priority":  "Required"
                                                                                                                    }
                                                                                                                ]
                                                                                                },
                                                                               "cart.product_not_found":  {
                                                                                                              "response":  {
                                                                                                                               "mode":  "ask_clarification",
                                                                                                                               "guidance":  "Indica que ese producto no se encontrÃ³ y pide una descripciÃ³n o referencia mÃ¡s precisa."
                                                                                                                           }
                                                                                                          },
                                                                               "cart.product_ambiguous":  {
                                                                                                              "response":  {
                                                                                                                               "mode":  "ask_clarification",
                                                                                                                               "guidance":  "Presenta Ãºnicamente los candidatos devueltos y pregunta cuÃ¡l referencia desea."
                                                                                                                           },
                                                                                                              "effects":  [
                                                                                                                              {
                                                                                                                                  "type": "presentation.add",
                                                                                                                                  "template": "product_ambiguity",
                                                                                                                                  "dataPath": "error.context",
                                                                                                                                  "mode": "Exclusive",
                                                                                                                                  "priority": "Required"
                                                                                                                              }
                                                                                                                          ]
                                                                                                          },
                                                                               "cart.insufficient_stock":  {
                                                                                                              "response":  {
                                                                                                                               "mode":  "ask_clarification",
                                                                                                                               "guidance":  "Explica con claridad la cantidad disponible y pide una cantidad valida; ningun cambio del lote fue aplicado."
                                                                                                                           },
                                                                                                              "effects":  [
                                                                                                                              {
                                                                                                                                  "type": "presentation.add",
                                                                                                                                  "template": "insufficient_stock",
                                                                                                                                  "dataPath": "error.context",
                                                                                                                                  "mode": "Exclusive",
                                                                                                                                  "priority": "Required"
                                                                                                                              }
                                                                                                                          ]
                                                                                                          },                                                                               "cart.item_not_found_or_ambiguous":  {
                                                                                                                        "response":  {
                                                                                                                                         "mode":  "ask_clarification",
                                                                                                                                         "guidance":  "Aclara cuÃ¡l producto existente del pedido desea modificar."
                                                                                                                                     }
                                                                                                                    },
                                                                               "cart.conflicting_commands":  {
                                                                                                                 "response":  {
                                                                                                                                  "mode":  "ask_clarification",
                                                                                                                                  "guidance":  "Pide aclarar el cambio final para el producto repetido; no se aplicÃ³ ningÃºn cambio del lote."
                                                                                                                              }
                                                                                                             },
                                                                               "cart.multiple_destinations":  {
                                                                                                                  "response":  {
                                                                                                                                   "mode":  "ask_clarification",
                                                                                                                                   "guidance":  "No se aplicÃ³ ningÃºn cambio. Pregunta cuÃ¡l direcciÃ³n debe usarse para entregar todo el Ãºnico pedido."
                                                                                                                               }
                                                                                                              }
                                                                           }
                                                         }
                                                     ],
                                         "response":  {
                                                          "fallbackTemplate":  "order_draft_unavailable"
                                                      }
                                     },
                                     {
                                         "id":  "order_data",
                                         "name":  "Entrega",
                                         "goal":  "Definir recogida o domicilio y obtener solo los datos faltantes requeridos por el checkout.",
                                         "advanceWhenFacts":  [
                                                                  "delivery_method",
                                                                  "city",
                                                                  "delivery_address",
                                                                  "delivery_phone",
                                                                  "customer_name"
                                                              ],
                                         "reentryOnFactChanged":  [
                                                                      "delivery_method",
                                                                      "city",
                                                                      "delivery_address",
                                                                      "delivery_phone",
                                                                      "customer_name"
                                                                  ],
                                         "conversationGuidance":  "Despues de que el cliente termine de agregar productos pregunta: Prefieres recoger tu pedido o recibirlo a domicilio? No muestres un resumen del carrito en esta etapa. Si elige recogida, registra delivery_method=recogida y usa como delivery_address el punto de recogida configurado o Punto de recogida CJ Distribuciones - Valledupar, Cesar. Si elige domicilio, registra delivery_method=domicilio y solicita solo datos faltantes: direccion, barrio o referencia cuando aplique, telefono si no existe y nombre del receptor si falta. No pidas datos confiables ya disponibles. No pidas ciudad si ya existe por defecto; usa Valledupar salvo que el cliente indique otra ciudad.",
                                         "collect":  [
                                                         "delivery_method",
                                                         "city",
                                                         "delivery_address",
                                                         "delivery_phone",
                                                         "customer_name",
                                                         "payment_method"
                                                     ]
                                     },
                                     {
                                         "id":  "payment_method",
                                         "name":  "Metodo de pago",
                                         "goal":  "Elegir uno de los metodos de pago configurados para CJ Distribuciones.",
                                         "advanceWhenFacts":  [
                                                                  "payment_method"
                                                              ],
                                         "conversationGuidance":  "Cuando la modalidad de entrega y datos requeridos esten completos, pregunta: Como deseas realizar el pago? Opciones configuradas: transferencia o efectivo. Registra payment_method=efectivo o payment_method=transferencia segun responda. No menciones metodos no configurados.",
                                         "collect":  [
                                                         "payment_method"
                                                     ]
                                     },
                                     {
                                         "id":  "summary",
                                         "name":  "Resumen final del pedido",
                                         "goal":  "Preparar y mostrar el resumen oficial con entrega, pago y total final del motor.",
                                         "advanceWhenFacts":  [
                                                                  "order_checkout_presented"
                                                              ],
                                         "reentryOnFactChanged":  [
                                                                      "order_finalized",
                                                                      "delivery_method",
                                                                      "city",
                                                                      "delivery_address",
                                                                      "delivery_phone",
                                                                      "customer_name",
                                                                      "payment_method"
                                                                  ],
                                         "actions":  [
                                                         {
                                                             "id":  "prepare_order_checkout",
                                                             "operation":  "commerce.prepare_checkout",
                                                             "trigger":  "when_ready",
                                                             "condition":  {
                                                                               "all":  [
                                                                                           {
                                                                                               "factPresent":  "order_finalized"
                                                                                           },
                                                                                           {
                                                                                               "factPresent":  "delivery_method"
                                                                                           },
                                                                                           {
                                                                                               "factPresent":  "city"
                                                                                           },
                                                                                           {
                                                                                               "factPresent":  "delivery_address"
                                                                                           },
                                                                                           {
                                                                                               "factPresent":  "delivery_phone"
                                                                                           },
                                                                                           {
                                                                                               "factPresent":  "customer_name"
                                                                                           },
                                                                                           {
                                                                                               "factPresent":  "payment_method"
                                                                                           },
                                                                                           {
                                                                                               "factMissing":  "order_checkout_presented"
                                                                                           }
                                                                                       ]
                                                                           },
                                                             "arguments":  {

                                                                           },
                                                             "onOutcome":  {
                                                                               "order.checkout_ready":  {
                                                                                                            "effects":  [
                                                                                                                            {
                                                                                                                                "type":  "fact.set",
                                                                                                                                "fact":  "order_checkout_presented",
                                                                                                                                "value":  true
                                                                                                                            }
                                                                                                                        ]
                                                                                                        },
                                                                               "order.checkout_payment_required":  {
                                                                                                                       "effects":  [
                                                                                                                                       {
                                                                                                                                           "type":  "fact.set",
                                                                                                                                           "fact":  "order_checkout_presented",
                                                                                                                                           "value":  true
                                                                                                                                       }
                                                                                                                                   ]
                                                                                                                   },
                                                                               "order.checkout_pending_manual_payment":  {
                                                                                                                             "effects":  [
                                                                                                                                             {
                                                                                                                                                 "type":  "fact.set",
                                                                                                                                                 "fact":  "order_checkout_presented",
                                                                                                                                                 "value":  true
                                                                                                                                             }
                                                                                                                                         ]
                                                                                                                         }
,
                                                                               "order_draft_missing":  {
                                                                                                            "response":  { "fallbackTemplate":  "order_draft_unavailable" },
                                                                                                            "effects":  [
                                                                                                                            {
                                                                                                                                "type":  "facts.clear",
                                                                                                                                "facts":  [ "order_finalized", "cart_review_confirmed", "order_checkout_presented" ]
                                                                                                                            }
                                                                                                                        ]
                                                                                                        },
                                                                               "missing_prerequisites":  {
                                                                                                              "response":  { "fallbackTemplate":  "order_draft_unavailable" },
                                                                                                              "effects":  [
                                                                                                                              {
                                                                                                                                  "type":  "facts.clear",
                                                                                                                                  "facts":  [ "order_finalized", "cart_review_confirmed", "order_checkout_presented" ]
                                                                                                                              }
                                                                                                                          ]
                                                                                                          }                                                                           }
                                                         }
                                                     ],
                                         "conversationGuidance":  "Cuando ya existan items, carrito aprobado, entrega y metodo de pago, el motor prepara el checkout una sola vez. Si el metodo es efectivo, muestra el resumen autoritativo renderizado por el motor y pide confirmacion verbal. Si el metodo es transferencia, muestra el resumen autoritativo renderizado por el motor e informa que el pago queda pendiente de confirmacion manual por el equipo; no pidas comprobante, no pidas confirmacion adicional del pedido y no confirmes que el pedido fue creado. Si falla por configuracion no recuperable, escala a humano.",
                                         "collect":  [
                                                         "order_checkout_presented"
                                                     ]
                                     },
                                     {
                                         "id":  "order_confirmation",
                                         "name":  "Confirmacion del pedido",
                                         "goal":  "Crear el pedido despues de confirmacion del cliente.",
                                         "advanceWhenFacts":  [
                                                                  "customer_confirmed"
                                                              ],
                                         "actions":  [
                                                         {
                                                             "id":  "create_confirmed_cash_order",
                                                             "operation":  "commerce.create_order",
                                                             "trigger":  "when_ready",
                                                             "condition":  {
                                                                               "all":  [
                                                                                           {
                                                                                               "factEquals":  {
                                                                                                                  "key":  "payment_method",
                                                                                                                  "value":  "efectivo"
                                                                                                              }
                                                                                           },
                                                                                           {
                                                                                               "factEquals":  {
                                                                                                                  "key":  "customer_confirmed",
                                                                                                                  "value":  true
                                                                                                              }
                                                                                           }
                                                                                       ]
                                                                           },
                                                             "arguments":  {
                                                                               "customer_confirmed":  "{{fact.customer_confirmed}}"
                                                                           },
                                                             "onOutcome":  {
                                                                               "order.created":  {
                                                                                                     "effects":  [
                                                                                                                     {
                                                                                                                         "type":  "sequence.enqueue",
                                                                                                                         "sequence":  "order_created_customer"
                                                                                                                     },
                                                                                                                     {
                                                                                                                         "type":  "request.complete"
                                                                                                                     }
                                                                                                                 ],
                                                                                                     "response":  {
                                                                                                                      "suppressText":  true
                                                                                                                  }
                                                                                                 }
                                                                           }
                                                         }
                                                     ],
                                         "conversationGuidance":  "Si payment_method=transferencia, no pidas confirmacion verbal, no confirmes que el pedido fue creado y responde que el pago queda pendiente de confirmacion manual por el equipo de CJ Distribuciones; cuando el pago se confirme manualmente, el sistema notificara que el pedido fue creado. Si payment_method=efectivo y falta customer_confirmed, pide confirmacion verbal del resumen final y registrala solo cuando el cliente la entregue claramente. Con customer_confirmed=true y metodo efectivo, crea el pedido usando los facts vigentes y despues envia la secuencia order_created_customer. Si corrige datos, metodo de pago o carrito, aplica el cambio y presenta resumen actualizado. No afirmes pago recibido solo por una imagen o comprobante si el workflow no lo valida.",
                                         "collect":  [
                                                         "customer_confirmed"
                                                     ]
                                     }
                                 ]
                  }
              ]
}';

IF ISJSON(@SettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedCJDistribuciones: SettingsJson invalido.', 1;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId)
BEGIN
    INSERT INTO dbo.Agents
        (AgentId, BusinessId, AgentTypeId, [Name], [Description], IsActive,
         SettingsJson, Model, Temperature, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Asistente CJ Distribuciones',
         N'Asistente comercial para pedidos, clasificacion de cliente y recetas web para hogar.',
         1, @SettingsJson, N'gpt-4.1-mini', 0.2, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE dbo.Agents
    SET BusinessId = @BusinessId,
        AgentTypeId = @AgentTypeId,
        [Name] = N'Asistente CJ Distribuciones',
        [Description] = N'Asistente comercial para pedidos, clasificacion de cliente y recetas web para hogar.',
        IsActive = 1,
        SettingsJson = @SettingsJson,
        Model = N'gpt-4.1-mini',
        Temperature = 0.4,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END

PRINT N'SeedCJDistribuciones: negocio, Mantis y agente configurados.';

GO
