-- =============================================================================

-- SeedSolorzanoAgentConfiguration.sql

--

-- Configuracion completa del agente Camila (Vinos Artesanales Solorzano) para

-- el motor agentic actual. Idempotente.

-- =============================================================================



SET QUOTED_IDENTIFIER ON;

SET ANSI_NULLS ON;

SET NOCOUNT ON;



DECLARE @BusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @AgentId    UNIQUEIDENTIFIER = 'B0EE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @WinePricesAttachmentId UNIQUEIDENTIFIER = 'C2A32E8B-6DB7-4D54-9C1C-06FCB7451C23';





IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)

BEGIN

    PRINT N'SeedSolorzanoAgentConfiguration: negocio Solorzano no encontrado; omitiendo.';

    RETURN;

END



IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @AgentId AND BusinessId = @BusinessId)

BEGIN

    PRINT N'SeedSolorzanoAgentConfiguration: agente Camila no encontrado para Solorzano; omitiendo.';

    RETURN;

END



DECLARE @SolorzanoWompiSettingsJson NVARCHAR(MAX) = N'{
  "mode": "production",
  "sandboxBaseUrl": "https://sandbox.wompi.co/v1",
  "productionBaseUrl": "https://production.wompi.co/v1",
  "requestTimeoutSeconds": 30,
  "checkoutBaseUrl": "https://checkout.wompi.co/l/"
}';

DECLARE @SolorzanoWompiSecretsJson NVARCHAR(MAX) = N'{"production":{"privateKey":"prv_prod_DxUoFd5FmncizjtVeuUqXqc0x1SJsxBX","publicKey":"pub_prod_5baNRvZ1ldAyfSAo0wA2W40LHlNWRkOZ","eventsSecret":"prod_events_1zuOTRdDmUihQsKy2xUim9XjNRPv1yBV","integritySecret":"prod_integrity_ZDf2JpYhsv1L9B6sACF60kFswwy3eKgx"}}';



IF ISJSON(@SolorzanoWompiSettingsJson) <> 1 OR ISJSON(@SolorzanoWompiSecretsJson) <> 1

BEGIN

    THROW 51000, 'SeedSolorzanoAgentConfiguration: Wompi JSON invalido.', 1;

END



MERGE dbo.IntegrationConnections AS target

USING (

    SELECT

        @BusinessId AS BusinessId,

        CAST(0 AS INT) AS ConnectionType,

        CAST(1 AS INT) AS Provider,

        CAST(1 AS INT) AS Capability,

        N'Wompi Vinos Solorzano' AS [Name],

        N'vinos-solorzano-production' AS AccountIdentifier,

        @SolorzanoWompiSettingsJson AS SettingsJson,

        @SolorzanoWompiSecretsJson AS SecretsJson,

        CAST(1 AS BIT) AS IsEnabled

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

        LastError = NULL,

        UpdatedAt = GETUTCDATE()

WHEN NOT MATCHED THEN

    INSERT (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],

            AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)

    VALUES (NEWID(), source.BusinessId, source.ConnectionType, source.Provider, source.Capability, source.[Name],

            source.AccountIdentifier, source.SettingsJson, source.SecretsJson, source.IsEnabled, GETUTCDATE());



PRINT N'SeedSolorzanoAgentConfiguration: Wompi productivo configurado para Solorzano.';



DECLARE @LocalCommerceConnectionId UNIQUEIDENTIFIER;



SELECT @LocalCommerceConnectionId = IntegrationConnectionId

FROM dbo.IntegrationConnections

WHERE BusinessId = @BusinessId

  AND ConnectionType = 1

  AND Provider = 0

  AND Capability = 0;



IF @LocalCommerceConnectionId IS NULL

BEGIN

    SET @LocalCommerceConnectionId = NEWID();

    INSERT INTO dbo.IntegrationConnections

        (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],

         AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)

    VALUES

        (@LocalCommerceConnectionId, @BusinessId, 1, 0, 0, N'Comercio local',

         N'local', N'{"currency":"COP","manageStock":false}', NULL, 1, GETUTCDATE());

END

ELSE

BEGIN

    UPDATE dbo.IntegrationConnections

    SET [Name] = N'Comercio local',

        AccountIdentifier = N'local',

        SettingsJson = N'{"currency":"COP","manageStock":false}',

        SecretsJson = NULL,

        IsEnabled = 1,

        UpdatedAt = GETUTCDATE()

    WHERE IntegrationConnectionId = @LocalCommerceConnectionId;

END



DECLARE @SolorzanoProducts TABLE

(

    ProductId UNIQUEIDENTIFIER NOT NULL,

    Sku NVARCHAR(100) NULL,

    [Name] NVARCHAR(250) NOT NULL,

    [Description] NVARCHAR(MAX) NULL,

    CategoryName NVARCHAR(150) NULL,

    UnitPrice DECIMAL(18, 2) NOT NULL,

    Currency NVARCHAR(10) NOT NULL,

    StockQuantity DECIMAL(18, 2) NULL,

    IsActive BIT NOT NULL,

    DisplayOrder INT NOT NULL

);



INSERT INTO @SolorzanoProducts

    (ProductId, Sku, [Name], [Description], CategoryName, UnitPrice, Currency, StockQuantity, IsActive, DisplayOrder)

VALUES

    ('100E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-DULCE-750',     N'Dulce 750ML',     N'Vino artesanal Solorzano dulce, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',     N'Vinos artesanales', 49900, N'COP', NULL, 0, 1),

    ('101E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-SEMIDULCE-750', N'Semidulce 750ML', N'Vino artesanal Solorzano semidulce, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.', N'Vinos artesanales', 49900, N'COP', NULL, 0, 2),

    ('102E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-SEMISECO-750',  N'Semiseco 750ML',  N'Vino artesanal Solorzano semiseco, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',  N'Vinos artesanales', 49900, N'COP', NULL, 0, 3),

    ('103E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-MANGO-750',     N'Mango 750ML',     N'Vino artesanal sabor mango en botella grande.', N'Vinos artesanales', 59900, N'COP', NULL, 1, 4),

    ('104E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-PREMIUM-750',   N'Premium 750ML',   N'Vino artesanal Solorzano premium, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',   N'Vinos artesanales', 69900, N'COP', NULL, 0, 5),

    ('105E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-DULCE-207',     N'Dulce 207ML',     N'Vino artesanal Solorzano dulce, botella 207ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',     N'Vinos artesanales', 22000, N'COP', NULL, 0, 6),

    ('106E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-SEMIDULCE-207', N'Semidulce 207ML', N'Vino artesanal Solorzano semidulce, botella 207ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.', N'Vinos artesanales', 22000, N'COP', NULL, 0, 7),

    ('107E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-MANGO-207',     N'Mango 207ML',     N'Vino artesanal en botella pequena.', N'Vinos artesanales', 25000, N'COP', NULL, 1, 8);

UPDATE odi

SET ProductId = NULL,

    UpdatedAt = GETUTCDATE()

FROM dbo.OrderDraftItems odi

LEFT JOIN @SolorzanoProducts sp ON sp.ProductId = odi.ProductId

WHERE odi.BusinessId = @BusinessId

  AND odi.ProductId IS NOT NULL

  AND sp.ProductId IS NULL;



UPDATE oi

SET ProductId = NULL,

    UpdatedAt = GETUTCDATE()

FROM dbo.OrderItems oi

LEFT JOIN @SolorzanoProducts sp ON sp.ProductId = oi.ProductId

WHERE oi.BusinessId = @BusinessId

  AND oi.ProductId IS NOT NULL

  AND sp.ProductId IS NULL;



MERGE dbo.Products AS target

USING @SolorzanoProducts AS source

   ON target.BusinessId = @BusinessId

  AND target.Sku = source.Sku

WHEN MATCHED THEN

    UPDATE SET

        IntegrationConnectionId = @LocalCommerceConnectionId,

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

    VALUES (source.ProductId, @BusinessId, @LocalCommerceConnectionId, NULL, 0, source.Sku, source.[Name],

            source.[Description], source.CategoryName, source.UnitPrice, source.Currency, 0, source.StockQuantity,

            source.IsActive, NULL, NULL, GETUTCDATE());



DELETE FROM dbo.Products

WHERE BusinessId = @BusinessId

  AND Source = 0

  AND NOT EXISTS (

      SELECT 1

      FROM @SolorzanoProducts sp

      WHERE sp.Sku = dbo.Products.Sku

  );



PRINT N'SeedSolorzanoAgentConfiguration: catalogo Solorzano actualizado con lista de precios Valledupar 2026.';

DELETE es

FROM dbo.EmployeeServices es

INNER JOIN dbo.Services s ON s.ServiceId = es.ServiceId

WHERE s.BusinessId = @BusinessId;



DELETE sru

FROM dbo.ServiceResourceUsages sru

INNER JOIN dbo.Services s ON s.ServiceId = sru.ServiceId

WHERE s.BusinessId = @BusinessId;



DELETE sbi

FROM dbo.ServiceBundleItems sbi

INNER JOIN dbo.Services s ON s.ServiceId = sbi.BundleServiceId OR s.ServiceId = sbi.IncludedServiceId

WHERE s.BusinessId = @BusinessId;



DELETE sar

FROM dbo.ServiceAddOnRules sar

INNER JOIN dbo.Services s ON s.ServiceId = sar.AddOnServiceId OR s.ServiceId = sar.CompatibleServiceId

WHERE s.BusinessId = @BusinessId;



DELETE ra

FROM dbo.ReservationAddOns ra

INNER JOIN dbo.Services s ON s.ServiceId = ra.AddOnServiceId

WHERE s.BusinessId = @BusinessId;





UPDATE r

SET ServiceId = NULL,

    UpdatedAt = GETUTCDATE()

FROM dbo.Reservations r

WHERE r.BusinessId = @BusinessId

  AND r.ServiceId IS NOT NULL;



DELETE e

FROM dbo.Enrollments e

INNER JOIN dbo.Services s ON s.ServiceId = e.ServiceId

WHERE s.BusinessId = @BusinessId;



DELETE FROM dbo.Services

WHERE BusinessId = @BusinessId;



IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessAttachmentId = @WinePricesAttachmentId)

BEGIN

    INSERT INTO dbo.BusinessAttachments

        (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)

    VALUES

        (@WinePricesAttachmentId, @BusinessId, N'Precios-vinos.jpeg', N'image', N'Precios-vinos.jpeg', N'Imagen de precios de vinos artesanales Solorzano', 1, GETUTCDATE());

END

ELSE

BEGIN

    UPDATE dbo.BusinessAttachments

    SET BusinessId = @BusinessId,

        BlobPath = N'Precios-vinos.jpeg',

        MediaType = N'image',

        Filename = N'Precios-vinos.jpeg',

        Description = N'Imagen de precios de vinos artesanales Solorzano',

        IsActive = 1

    WHERE BusinessAttachmentId = @WinePricesAttachmentId;

END






DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.68,
  "historyWindowSize": 24,
  "commerce": {
    "enabled": true,
    "provider": "Local"
  },
  "operatingHours": {
    "enforce": true,
    "outsideHours": {
      "guidance": "Responde de forma breve, cordial y cerrada. Explica que el negocio esta fuera de horario y que el proximo horario habil es {{next_operating_window}}. Adapta el mensaje a lo que dijo el cliente, pero no solicites datos, no prometas ejecutar gestiones, no abras catalogos y no termines con preguntas."
    }
  },
  "persona": "Eres el asistente comercial de Vinos Artesanales Solorzano. Atiendes en espanol con tono humano, cercano y confiable, guiando la compra sin presion.\n\nResponde claro y breve. Para datos, opciones, resumen, envio o pago, usa listas cortas con campos claros.",
  "policies": "## PRODUCTO\n\n- Comunica que los vinos artesanales Solorzano no son elaborados a base de uva y tienen 12 grados de alcohol cuando sea relevante.\n- Para catalogo, precios, tamanos, sabores y disponibilidad, no repitas listas del historial: usa solo productos activos devueltos por la busqueda de productos oficiales en el turno vigente.\n\n## APERTURA\n\n- En cada apertura del dia, saluda natural, da la bienvenida a Vinos Artesanales Solorzano y presenta brevemente que somos productores de vinos elaborados con fruta seleccionada de nuestra region.\n- Usa el nombre del cliente si esta disponible.\n- Si el cliente ya pidio algo, usa solo una apertura breve antes de continuar con esa intencion.\n- Despues del saludo, sigue de forma natural con lo que el cliente pidio.\n- No uses saludos largos.",
  "messageSequences": {
    "wine_prices_image": {
      "messages": [
        {
          "attachmentId": "C2A32E8B-6DB7-4D54-9C1C-06FCB7451C23"
        }
      ]
    },
    "order_paid_customer": {
      "messages": [
        {
          "body": "Gracias por tu compra, {customer_name}. Recibimos el pago del pedido {order_number} por ${total} {currency}. Ya estamos coordinando el domicilio y te avisaremos si necesitamos algo adicional."
        }
      ]
    },
    "order_created_customer": {
      "messages": [
        {
          "body": "Gracias por preferirnos. Tu pedido quedo confirmado y ya estamos coordinando el domicilio. Te avisaremos si necesitamos algo adicional."
        }
      ]
    },
    "delivery_request": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "delivery_request",
          "language": "es_CO",
          "headerParameters": [
            "{business_name}"
          ],
          "bodyParameters": [
            "{attempt_code}",
            "{order_number}",
            "{pickup_contact_name}",
            "{pickup_address}",
            "{customer_name}",
            "{customer_phone}",
            "{city}",
            "{delivery_address}",
            "{total}",
            "{currency}",
            "{payment_method}"
          ],
          "buttons": [
            {
              "id": "external_interaction:accepted:{external_interaction_id}",
              "title": "Aceptar"
            },
            {
              "id": "external_interaction:declined:{external_interaction_id}",
              "title": "No tomar"
            }
          ]
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
    "delivery_requested": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "delivery_requested",
          "language": "es_CO",
          "bodyParameters": [
            "{order_number}",
            "{attempt_code}",
            "{contact_name}",
            "{contact_phone}",
            "{city}",
            "{delivery_address}",
            "{total}",
            "{currency}"
          ]
        }
      ]
    },
    "delivery_confirmed": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "delivery_confirmed",
          "language": "es_CO",
          "bodyParameters": [
            "{order_number}",
            "{attempt_code}",
            "{contact_name}",
            "{contact_phone}",
            "{customer_name}",
            "{customer_phone}",
            "{delivery_address}"
          ]
        }
      ]
    },
    "delivery_unavailable": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "delivery_unavailable",
          "language": "es_CO",
          "bodyParameters": [
            "{order_number}",
            "{customer_name}",
            "{customer_phone}",
            "{city}",
            "{delivery_address}",
            "{items}",
            "{total}",
            "{currency}",
            "{attempt_code}"
          ]
        }
      ]
    }
  },
  "globalActions": [
    {
      "id": "human_handoff",
      "priority": 100,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja grave, distribuidor/mayorista o una situacion fuera del flujo normal.",
      "conversationGuidance": "Responde con una frase corta y cordial. Para distribuidor, menciona minimo 12 unidades y margen 25%. Luego escala a humano.",
      "signal": {
        "type": "human_escalation",
        "description": "Escalar a humano cuando el cliente lo pida, haya queja grave, distribuidor/mayorista o una situacion fuera del flujo normal.",
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
      "id": "modify_current_order",
      "priority": 90,
      "goal": "Modificar el carrito actual cuando el cliente, despues de decir que no agregaba mas o despues de recibir el resumen/link, pida agregar, quitar o cambiar productos/cantidades.",
      "conversationGuidance": "Si el cliente quiere agregar, quitar, reducir cantidades, cambiar productos o ver opciones para modificar el pedido actual, esta accion tiene prioridad sobre pedir datos de envio o verificar pago. Si ya hubo resumen o link de pago, primero modifica el carrito y luego genera un resumen/link nuevo. Para cualquier cambio sobre carrito ya existente, consulta el pedido actual primero. Para cambiar la cantidad total de un producto ya en carrito, actualiza la cantidad con quantity igual a la cantidad final deseada. Para quitar un item completo, quita el producto sin quantity; para reducirlo a una cantidad final menor, quita producto con quantity igual a la cantidad final deseada. Si hay varios items y la referencia del producto queda ambigua, pregunta una sola vez usando los nombres de los productos del carrito. Para agregar producto nuevo o unidades adicionales, agrega solo producto vigente devuelto por busqueda de productos oficiales; si falta producto o la referencia viene de una lista anterior, consulta productos oficiales; si falta cantidad, pregunta solo cuantas unidades. Si pide otro tamano, presentacion, sabor u opciones parecidas, consulta productos oficiales antes de responder y menciona solo alternativas devueltas por la herramienta. Despues de agregar, quitar o actualizar cantidad exitosamente, consulta el pedido actual y muestra el carrito actualizado. Si el carrito quedo vacio, ayuda a elegir producto. Si el carrito tiene items y ya existen city, delivery_address, delivery_phone, customer_name y payment_method, registra order_finalized=true y luego prepara el resumen del pedido en el mismo turno para recalcular total y link; presenta el nuevo resumen/link como la version vigente del pedido. Si faltan datos de envio o metodo de pago, pide solo lo faltante.",
      "signal": {
        "type": "order_changes",
        "description": "Cambios concretos sobre el ?nico pedido activo.",
        "valueSchema": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "operation": {
                "type": "string",
                "enum": [
                  "add",
                  "remove",
                  "set_quantity"
                ]
              },
              "productText": {
                "type": "string"
              },
              "quantity": {
                "anyOf": [
                  {
                    "type": "number"
                  },
                  {
                    "type": "null"
                  }
                ]
              },
              "destinationReference": {
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
              "operation",
              "productText",
              "quantity",
              "destinationReference"
            ]
          }
        }
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
            "cart.applied": {},
            "cart.multiple_destinations": {
              "response": {
                "mode": "ask_clarification"
              }
            },
            "cart.product_ambiguous": {
              "response": {
                "mode": "ask_clarification"
              }
            },
            "cart.item_not_found_or_ambiguous": {
              "response": {
                "mode": "ask_clarification"
              }
            }
          }
        }
      ]
    },
    {
      "id": "restart_order",
      "priority": 70,
      "goal": "Reiniciar el pedido actual si el cliente cambia completamente de producto o quiere empezar de nuevo.",
      "conversationGuidance": "Reinicia la solicitud solo cuando el cliente indique claramente que quiere cambiar el pedido completo, empezar de cero, cancelar el pedido anterior o hacer otro pedido independiente. Si habia resumen o link pendiente y el cliente abandona ese pedido, usa checkout_action=abandon. Para agregar o quitar productos del pedido actual, usa la accion transversal de modificar pedido.",
      "signal": {
        "type": "restart_request",
        "description": "Reiniciar el pedido actual si el cliente cambia completamente de producto o quiere empezar de nuevo.",
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
                    "occasion",
                    "order_finalized",
                    "gift_wrap",
                    "city",
                    "delivery_address",
                    "payment_method",
                    "order_checkout_presented",
                    "customer_confirmed",
                    "shipping_cost"
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
      "key": "occasion",
      "role": "order.occasion",
      "label": "ocasion del vino",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "order_finalized",
      "role": "order.finalized",
      "label": "cliente finalizo el carrito",
      "type": "boolean",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "gift_wrap",
      "role": "order.gift_wrap",
      "label": "tula de regalo",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "city",
      "role": "shipping.city",
      "label": "ciudad de entrega",
      "type": "string",
      "required": true,
      "source": "system",
      "defaultValue": "Valledupar",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "delivery_address",
      "role": "shipping.address",
      "label": "direccion de entrega",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "delivery_phone",
      "role": "customer.phone",
      "label": "celular de entrega",
      "type": "phone",
      "required": true,
      "source": "user",
      "scope": "customer"
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
      "key": "payment_method",
      "role": "payment.method",
      "label": "metodo de pago",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 1
    },
    {
      "key": "order_checkout_presented",
      "role": "order.checkout_presented",
      "label": "resumen presentado",
      "type": "boolean",
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
      "retentionDays": 1,
      "dependsOn": [
        "order_checkout_presented",
        "order_finalized",
        "city",
        "delivery_address",
        "delivery_phone",
        "customer_name",
        "payment_method"
      ]
    },
    {
      "key": "shipping_cost",
      "role": "shipping.cost",
      "label": "costo de envio",
      "type": "number",
      "required": false,
      "source": "system",
      "scope": "request",
      "retentionDays": 1
    }
  ],
  "notifications": {
    "reservation_created": {
      "enabled": false,
      "recipients": [
        "+573004442469"
      ],
      "sendMessageSequence": null
    },
    "order_created": {
      "enabled": true,
      "recipients": [
        "+573004442469"
      ],
      "sendMessageSequence": "order_created"
    },
    "delivery_requested": {
      "enabled": true,
      "recipients": [
        "+573004442469"
      ],
      "sendMessageSequence": "delivery_requested"
    },
    "delivery_confirmed": {
      "enabled": true,
      "recipients": [
        "+573004442469"
      ],
      "sendMessageSequence": "delivery_confirmed"
    },
    "delivery_unavailable": {
      "enabled": true,
      "recipients": [
        "+573004442469"
      ],
      "sendMessageSequence": "delivery_unavailable"
    }
  },
  "webhooks": {
    "wompi": {
      "order_paid": {
        "sendMessageSequence": "order_paid_customer"
      }
    }
  },
  "escalations": {
    "human": {
      "contacts": [
        "+573004442469"
      ]
    },
    "external": {
      "enabled": true,
      "events": {
        "order_created": {
          "enabled": false,
          "contactType": "domicilio",
          "pickupAddress": "Centro Comercial Guatapuri, Isla Vino Solorzano (frente a TOTTO)",
          "attemptTimeoutMinutes": 15,
          "attemptCodePrefix": "PED",
          "sendMessageSequence": "delivery_request",
          "outcomeEvents": {
            "requested": "delivery_requested",
            "accepted": "delivery_confirmed",
            "declined": "delivery_unavailable",
            "timed_out": "delivery_unavailable"
          },
          "contacts": [
            {
              "businessInboundContactId": "E2EE3BA9-E6BF-43E2-8C1A-560CB724688B",
              "priority": 1
            }
          ]
        }
      }
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {
      "order": {
        "paymentMethods": {
          "efectivo": {
            "label": "efectivo al recibir",
            "aliases": [
              "efectivo"
            ],
            "template": "order_checkout_no_payment"
          },
          "transferencia": {
            "label": "transferencia con link de pago",
            "aliases": [
              "transferencia",
              "link de pago"
            ],
            "payment": {
              "percentage": 100
            },
            "template": "order_checkout_with_payment",
            "confirmationOutcome": "order_paid"
          }
        },
        "shipping": {
          "enabled": true,
          "localCity": "Valledupar",
          "localCost": 6000,
          "nationalCost": 80000
        }
      }
    }
  },
  "templates": {
    "order_checkout_with_payment": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total a pagar: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nMetodo de pago: transferencia (link de pago)\n\nPuedes pagar de forma segura en este enlace:\n{{link_url}}\n\nCuando el pago sea aprobado, te confirmaremos la compra automaticamente.",
    "order_checkout_no_payment": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total a pagar: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nMetodo de pago: efectivo al recibir\n\nConfirmas tu pedido con esta informacion?"
  },
  "flows": [
    {
      "id": "order",
      "type": "primary",
      "routingGuidance": "Use this primary flow for new wine orders, product selection, delivery data, payment method, checkout summary and order confirmation.",
      "stages": [
        {
          "id": "discovery",
          "name": "Descubrimiento y recomendacion",
          "goal": "Presentar el catalogo inicial sin precios y construir el carrito hasta que el cliente finalice la compra.",
          "advanceWhenFacts": [
            "order_finalized"
          ],
          "conversationGuidance": "En saludos o informacion inicial, usa la tool search_products con query vino y limit 10. Luego presenta hasta 10 productos activos devueltos en el turno vigente sin mencionar precios, y cierra exactamente con: Que vino te gustaria degustar el dia de hoy?. Para recomendaciones por ocasion, opciones, precios, promociones, tamanos, presentaciones o sabores, consulta productos oficiales antes de responder; usa query vino, limit 5 cuando la ocasion sea general. Si el cliente selecciona por numero, precio, tamano, sabor, nombre parcial o descripcion, resuelve solo contra productos activos devueltos en el turno vigente; si no hay resultado vigente o la referencia no aparece, consulta productos oficiales antes de responder o agregar. Si hay una coincidencia razonable pero falta cantidad, pregunta cuantas unidades quiere. Agrega al carrito solo cuando producto y cantidad expresa esten claros. Despues de agregar producto exitosamente, muestra el carrito y pregunta: Quieres agregar algo mas a la compra? Si responde con una negacion y existe al menos un item, registra order_finalized=true; si el carrito esta vacio, ayuda a elegir un producto primero.",
          "collect": [
            "order_finalized"
          ],
          "signals": [
            {
              "type": "product_search",
              "description": "Consulta concreta de productos oficiales.",
              "valueSchema": {
                "type": "string"
              }
            },
            {
              "type": "cart_changes",
              "description": "Cambios concretos del ?nico pedido activo.",
              "valueSchema": {
                "type": "array",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "operation": {
                      "type": "string",
                      "enum": [
                        "add",
                        "remove",
                        "set_quantity"
                      ]
                    },
                    "productText": {
                      "type": "string"
                    },
                    "quantity": {
                      "anyOf": [
                        {
                          "type": "number"
                        },
                        {
                          "type": "null"
                        }
                      ]
                    },
                    "destinationReference": {
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
                    "operation",
                    "productText",
                    "quantity",
                    "destinationReference"
                  ]
                }
              }
            }
          ],
          "actions": [
            {
              "id": "commerce_search_products_1",
              "operation": "commerce.search_products",
              "arguments": {
                "query": "{{user.message}}",
                "limit": 10
              },
              "onOutcome": {
                "products.found": {}
              }
            },
            {
              "id": "search_products",
              "operation": "commerce.search_products",
              "trigger": "on_signal",
              "signal": "product_search",
              "arguments": {
                "query": "{{signal.product_search.value}}"
              },
              "onOutcome": {
                "products.found": {}
              }
            },
            {
              "id": "apply_cart_changes",
              "operation": "commerce.apply_order_changes",
              "trigger": "on_signal",
              "signal": "cart_changes",
              "arguments": {
                "commands": "{{signal.cart_changes.value}}"
              },
              "onOutcome": {
                "cart.applied": {},
                "cart.multiple_destinations": {
                  "response": {
                    "mode": "ask_clarification"
                  }
                },
                "cart.product_ambiguous": {
                  "response": {
                    "mode": "ask_clarification"
                  }
                },
                "cart.item_not_found_or_ambiguous": {
                  "response": {
                    "mode": "ask_clarification"
                  }
                }
              }
            }
          ]
        },
        {
          "id": "order_data",
          "name": "Datos del pedido",
          "goal": "Recoger direccion de entrega, celular y nombre de quien recibe para coordinar envio. La ciudad por defecto es Valledupar.",
          "advanceWhenFacts": [
            "city",
            "delivery_address",
            "delivery_phone",
            "customer_name"
          ],
          "reentryOnFactChanged": [
            "city",
            "delivery_address",
            "delivery_phone",
            "customer_name"
          ],
          "conversationGuidance": "Pide en una sola lista solo los datos de usuario que falten: Direccion de entrega, Celular de contacto y Nombre de quien recibe. No pidas ciudad si ya existe por defecto; usa Valledupar como ciudad local salvo que el cliente indique otra. Cuando el cliente responda, registra todos los datos que entregue en ese turno. Si todavia falta algun dato requerido, pide solo los faltantes juntos.",
          "collect": [
            "city",
            "delivery_address",
            "delivery_phone",
            "customer_name"
          ]
        },
        {
          "id": "payment_method",
          "name": "Metodo de pago",
          "goal": "Preguntar si el cliente pagara en efectivo al recibir o por transferencia con link despues de confirmar los datos de envio.",
          "advanceWhenFacts": [
            "payment_method"
          ],
          "conversationGuidance": "Cuando ya existan items y datos completos de entrega, pregunta una sola cosa con estas dos opciones: efectivo al recibir o transferencia con link de pago. Si responde efectivo, registra payment_method=efectivo. Si responde transferencia o link de pago, registra payment_method=transferencia. Despues de guardar el metodo de pago, continua al resumen.",
          "collect": [
            "payment_method"
          ]
        },
        {
          "id": "summary",
          "name": "Resumen del pedido",
          "goal": "Preparar y mostrar el resumen oficial del pedido segun el metodo de pago configurado.",
          "advanceWhenFacts": [
            "order_checkout_presented"
          ],
          "reentryOnFactChanged": [
            "order_finalized",
            "city",
            "delivery_address",
            "delivery_phone",
            "customer_name",
            "payment_method"
          ],
          "conversationGuidance": "Cuando ya existan items, datos de entrega y metodo de pago, prepara el resumen del pedido una sola vez para calcular envio, total y renderizar el resumen oficial. Si devuelve resumen sin link, muestra el resumen y pide confirmacion verbal. Si devuelve link de pago, muestra el resumen/link y espera confirmacion automatica del webhook. Si falla por configuracion de pago, link de pago o error no recuperable, responde breve y escala a humano en ese mismo turno.",
          "collect": [
            "order_checkout_presented"
          ],
          "actions": [
            {
              "id": "commerce_prepare_checkout_1",
              "operation": "commerce.prepare_checkout",
              "arguments": {},
              "onOutcome": {
                "order.checkout_ready": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                },
                "order.checkout_payment_required": {
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
                "order.checkout_prepared": {
                  "effects": [
                    {
                      "type": "fact.set",
                      "fact": "order_checkout_presented",
                      "value": true
                    }
                  ]
                }
              }
            }
          ]
        },
        {
          "id": "order_confirmation",
          "name": "Confirmacion del pedido",
          "goal": "Confirmar el pedido ya resumido o acompanar el pago pendiente segun el metodo elegido.",
          "advanceWhenFacts": [
            "customer_confirmed"
          ],
          "conversationGuidance": "Si falta customer_confirmed, pide confirmacion verbal del resumen final y registrala solo cuando el cliente la entregue claramente. Si payment_method=efectivo y customer_confirmed=true, crea el pedido con los facts vigentes; cuando el pedido quede creado, envia la secuencia order_created_customer. Si payment_method=transferencia, espera la confirmacion automatica del webhook; si el cliente pregunta por el pago, verifica el pago. Si el cliente corrige datos, metodo de pago o carrito, aplica el cambio con la accion transversal de modificar pedido y presenta el resumen recalculado.",
          "collect": [
            "customer_confirmed"
          ]
        }
      ]
    }
  ]
}';







IF ISJSON(@SettingsJson) <> 1

BEGIN

    THROW 51000, 'SeedSolorzanoAgentConfiguration: SettingsJson invalido.', 1;

END



UPDATE dbo.Agents

SET SettingsJson         = @SettingsJson,

    Model                = N'gpt-4.1-mini',

    Temperature          = 0.68,

    Description          = N'Asesora comercial Vinos Artesanales Solorzano: venta por WhatsApp con pedido, envio y pago.',

    IsActive             = 1,

    UpdatedAt            = GETUTCDATE()

WHERE AgentId = @AgentId;



PRINT N'SeedSolorzanoAgentConfiguration: Camila reconfigurada para negocio ' + CAST(@BusinessId AS NVARCHAR(36));

GO
