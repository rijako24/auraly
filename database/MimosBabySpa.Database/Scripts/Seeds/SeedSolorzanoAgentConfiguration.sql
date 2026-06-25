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
DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

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

DECLARE @SourceWompiConnectionId UNIQUEIDENTIFIER;
DECLARE @ExistingSolorzanoWompiId UNIQUEIDENTIFIER;

SELECT @ExistingSolorzanoWompiId = IntegrationConnectionId
FROM dbo.IntegrationConnections
WHERE BusinessId = @BusinessId
  AND ConnectionType = 0
  AND Provider = 1
  AND Capability = 1
  AND IsEnabled = 1
  AND NULLIF(SecretsJson, N'{}') IS NOT NULL;

SELECT @SourceWompiConnectionId = IntegrationConnectionId
FROM dbo.IntegrationConnections
WHERE BusinessId = @MimosBusinessId
  AND ConnectionType = 0
  AND Provider = 1
  AND Capability = 1
  AND IsEnabled = 1
  AND NULLIF(SecretsJson, N'{}') IS NOT NULL;

IF @ExistingSolorzanoWompiId IS NOT NULL
BEGIN
    PRINT N'SeedSolorzanoAgentConfiguration: Wompi propio de Solorzano preservado.';
END
ELSE IF @SourceWompiConnectionId IS NOT NULL
BEGIN
    MERGE dbo.IntegrationConnections AS target
    USING (
        SELECT
            @BusinessId AS BusinessId,
            ConnectionType,
            Provider,
            Capability,
            [Name],
            AccountIdentifier,
            SettingsJson,
            SecretsJson,
            IsEnabled
        FROM dbo.IntegrationConnections
        WHERE IntegrationConnectionId = @SourceWompiConnectionId
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
            UpdatedAt = GETUTCDATE()
    WHEN NOT MATCHED THEN
        INSERT (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],
                AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
        VALUES (NEWID(), source.BusinessId, source.ConnectionType, source.Provider, source.Capability, source.[Name],
                source.AccountIdentifier, source.SettingsJson, source.SecretsJson, source.IsEnabled, GETUTCDATE());

    PRINT N'SeedSolorzanoAgentConfiguration: Wompi copiado desde Mimos para Solorzano.';
END
ELSE
BEGIN
    PRINT N'SeedSolorzanoAgentConfiguration: Wompi de Mimos no encontrado; omitiendo copia para Solorzano.';
END

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
    DisplayOrder INT NOT NULL
);

INSERT INTO @SolorzanoProducts
    (ProductId, Sku, [Name], [Description], CategoryName, UnitPrice, Currency, StockQuantity, DisplayOrder)
VALUES
    ('100E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-DULCE-750',     N'Dulce 750ML',     N'Vino artesanal Solorzano dulce, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',     N'Vinos artesanales', 49900, N'COP', NULL, 1),
    ('101E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-SEMIDULCE-750', N'Semidulce 750ML', N'Vino artesanal Solorzano semidulce, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.', N'Vinos artesanales', 49900, N'COP', NULL, 2),
    ('102E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-SEMISECO-750',  N'Semiseco 750ML',  N'Vino artesanal Solorzano semiseco, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',  N'Vinos artesanales', 49900, N'COP', NULL, 3),
    ('103E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-MANGO-750',     N'Mango 750ML',     N'Vino artesanal Solorzano sabor mango, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.', N'Vinos artesanales', 59900, N'COP', NULL, 4),
    ('104E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-PREMIUM-750',   N'Premium 750ML',   N'Vino artesanal Solorzano premium, botella 750ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',   N'Vinos artesanales', 69900, N'COP', NULL, 5),
    ('105E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-DULCE-207',     N'Dulce 207ML',     N'Vino artesanal Solorzano dulce, botella 207ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.',     N'Vinos artesanales', 22000, N'COP', NULL, 6),
    ('106E3BA9-E6BF-43E2-8C1A-560CB724688B', N'SOL-SEMIDULCE-207', N'Semidulce 207ML', N'Vino artesanal Solorzano semidulce, botella 207ML. Producto de fruta seleccionada de la region, 12 grados de alcohol.', N'Vinos artesanales', 22000, N'COP', NULL, 7);

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
        IsActive = 1,
        RawPayloadJson = NULL,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (ProductId, BusinessId, IntegrationConnectionId, ExternalProductId, Source, Sku, [Name],
            [Description], CategoryName, UnitPrice, Currency, ManageStock, StockQuantity,
            IsActive, RawPayloadJson, LastSyncedAt, CreatedAt)
    VALUES (source.ProductId, @BusinessId, @LocalCommerceConnectionId, NULL, 0, source.Sku, source.[Name],
            source.[Description], source.CategoryName, source.UnitPrice, source.Currency, 0, source.StockQuantity,
            1, NULL, NULL, GETUTCDATE());

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

UPDATE pt
SET Snapshot_ServiceId = NULL
FROM dbo.PaymentTransactions pt
INNER JOIN dbo.Services s ON s.ServiceId = pt.Snapshot_ServiceId
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

DECLARE @SystemPrompt NVARCHAR(MAX) = N'';

DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.68,
  "maxToolIterations": 8,
  "historyWindowSize": 24,
  "consecutiveErrorEscalationThreshold": 3,
  "commerce": {
    "enabled": true,
    "provider": "Local"
  },
  "persona": "Eres el asistente comercial de Vinos Artesanales Solorzano. Atiendes en espanol con tono humano, cercano y confiable, guiando la compra sin presion.\n\nResponde claro y breve. Para datos, opciones, resumen, envio o pago, usa listas cortas con campos claros.",
  "policies": "## PRODUCTO\n\n- Comunica que los vinos artesanales Solorzano no son elaborados a base de uva y tienen 12 grados de alcohol cuando sea relevante.",
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
  "flow": {
    "stageDetection": "automatic",
    "stages": [
      {
        "id": "discovery",
        "name": "Descubrimiento y recomendacion",
        "goal": "Dar la bienvenida, presentar el catalogo inicial sin precios y construir el carrito hasta que el cliente finalice la compra.",
        "hint": "En saludos o informacion inicial, primero llama search_products con query vino y limit 10. Responde dando la bienvenida a Vinos Artesanales Solorzano e indica: somos productores de vinos elaborados con fruta seleccionada de nuestra region. Luego presenta hasta 10 productos activos devueltos por search_products sin mencionar precios, y cierra exactamente con: Que vino te gustaria degustar el dia de hoy?. Para recomendaciones por ocasion, opciones, precios, promos, tamanos, presentaciones o sabores, llama search_products antes de responder; usa query vino, limit 5 cuando la ocasion sea general. Si el cliente selecciona por numero, precio, tamano, sabor, nombre parcial o descripcion, resuelve contra el ultimo search_products; si hay una coincidencia razonable pero falta cantidad, pregunta cuantas unidades quiere. Agrega al carrito solo cuando producto y cantidad expresa esten claros. Despues de add_order_item exitoso, muestra el carrito y pregunta: Quieres agregar algo mas a la compra? Si responde con una negacion y existe al menos un item, llama set_fact order_finalized=true; si el carrito esta vacio, ayuda a elegir un producto primero.",
        "allowedTools": [
          "search_products",
          "add_order_item",
          "set_fact"
        ],
        "afterTool": [
          {
            "tool": "search_products",
            "when": {
              "path": "ok",
              "equals": "true"
            },
            "sendMessageSequence": "wine_prices_image",
            "sendOncePerConversation": true
          }
        ],
        "advanceWhenFacts": [
          "order_finalized"
        ]
      },
      {
        "id": "order_data",
        "name": "Datos del pedido",
        "goal": "Recoger direccion de entrega, celular y nombre de quien recibe para coordinar envio. La ciudad por defecto es Valledupar.",
        "hint": "Pide en una sola lista solo los datos de usuario que falten: Direccion de entrega, Celular de contacto y Nombre de quien recibe. No pidas ciudad si ya existe por defecto; usa Valledupar como ciudad local salvo que el cliente indique otra. Cuando el cliente responda, registra con set_fact todos los datos que entregue en ese turno. Si todavia falta algun dato requerido, pide solo los faltantes juntos.",
        "allowedTools": [
          "set_fact"
        ],
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
        ]
      },
      {
        "id": "payment_method",
        "name": "Metodo de pago",
        "goal": "Preguntar si el cliente pagara en efectivo al recibir o por transferencia con link despues de confirmar los datos de envio.",
        "hint": "Cuando ya existan items y datos completos de entrega, pregunta una sola cosa con estas dos opciones: efectivo al recibir o transferencia con link de pago. Si responde efectivo, registra payment_method=efectivo. Si responde transferencia o link de pago, registra payment_method=transferencia. Despues de guardar el metodo de pago, continua al resumen.",
        "allowedTools": [
          "set_fact",
          "get_order_draft"
        ],
        "advanceWhenFacts": [
          "payment_method"
        ]
      },
      {
        "id": "summary",
        "name": "Resumen del pedido",
        "goal": "Preparar y mostrar el resumen oficial del pedido segun el metodo de pago configurado.",
        "hint": "Cuando ya existan items, datos de entrega y metodo de pago, llama prepare_order_checkout una sola vez para calcular envio, total y renderizar el resumen oficial. Si devuelve resumen sin link, muestra el resumen y pide confirmacion verbal. Si devuelve link de pago, muestra el resumen/link y espera confirmacion automatica del webhook. Si falla por configuracion de pago, link de pago o error no recuperable, responde breve y llama escalate_to_human en ese mismo turno.",
        "allowedTools": [
          "prepare_order_checkout",
          "get_order_draft",
          "escalate_to_human"
        ],
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
        "afterTool": [
          {
            "tool": "prepare_order_checkout",
            "when": {
              "path": "ok",
              "equals": "true"
            },
            "setFacts": {
              "order_checkout_presented": "true"
            }
          }
        ]
      },
      {
        "id": "order_confirmation",
        "name": "Confirmacion del pedido",
        "goal": "Confirmar el pedido ya resumido o acompanar el pago pendiente segun el metodo elegido.",
        "hint": "Si payment_method=efectivo y el cliente confirma claramente el resumen, llama create_order con customer_confirmed=true, customer_name, customer_phone y delivery_address; cuando create_order confirme el pedido, llama send_message_sequence con sequence=order_created_customer. Si payment_method=transferencia, espera la confirmacion automatica del webhook; si el cliente pregunta por el pago, llama verify_payment. Si el cliente corrige datos, metodo de pago o carrito, aplica el cambio con la accion transversal de modificar pedido y presenta el resumen recalculado.",
        "allowedTools": [
          "create_order",
          "send_message_sequence",
          "verify_payment",
          "get_order_draft",
          "set_fact",
          "escalate_to_human"
        ],
        "advanceWhenFacts": []
      }
    ]
  },
  "globalActions": [
    {
      "id": "human_handoff",
      "priority": 100,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja grave, distribuidor/mayorista o una situacion fuera del flujo normal.",
      "hint": "Responde con una frase corta y cordial. Para distribuidor, menciona minimo 12 unidades y margen 25%. Luego llama escalate_to_human.",
      "allowedTools": [
        "escalate_to_human"
      ]
    },
    {
      "id": "modify_current_order",
      "priority": 90,
      "goal": "Modificar el carrito actual cuando el cliente, despues de decir que no agregaba mas o despues de recibir el resumen/link, pida agregar, quitar o cambiar productos/cantidades.",
      "hint": "Si el cliente quiere agregar, quitar, reducir cantidades, cambiar productos o ver opciones para modificar el pedido actual, esta accion tiene prioridad sobre pedir datos de envio o verificar pago. Si ya hubo resumen o link de pago, primero modifica el carrito y luego genera un resumen/link nuevo. Para cualquier cambio sobre carrito ya existente, llama get_order_draft primero. Para cambiar la cantidad total de un producto ya en carrito (ej: mejor quiero llevar 3, dejalo en 3, cambia a 3), llama update_order_item_quantity con quantity igual a la cantidad final deseada. Para quitar un item completo, llama remove_order_item sin quantity; para reducirlo a una cantidad final menor, llama remove_order_item con quantity igual a la cantidad final deseada. Si hay varios items y la referencia del producto queda ambigua, pregunta una sola vez usando los nombres de los productos del carrito. Para agregar producto nuevo o unidades adicionales (ej: agrega 3 mas), llama add_order_item; si falta producto, llama search_products; si falta cantidad, pregunta solo cuantas unidades. Si pide otro tamano, presentacion, sabor u opciones parecidas, llama search_products antes de responder y menciona solo alternativas devueltas por la herramienta. Despues de add_order_item, remove_order_item o update_order_item_quantity exitoso, llama get_order_draft y muestra el carrito actualizado. Si el carrito quedo vacio, ayuda a elegir producto. Si el carrito tiene items y ya existen city, delivery_address, delivery_phone, customer_name y payment_method, llama set_fact order_finalized=true y luego prepare_order_checkout en el mismo turno para recalcular total y link; presenta el nuevo resumen/link como la version vigente del pedido. Si faltan datos de envio o metodo de pago, pide solo lo faltante.",
      "allowedTools": [
        "search_products",
        "add_order_item",
        "remove_order_item",
        "update_order_item_quantity",
        "get_order_draft",
        "set_fact",
        "prepare_order_checkout"
      ]
    },
    {
      "id": "restart_order",
      "priority": 70,
      "goal": "Reiniciar el pedido actual si el cliente cambia completamente de producto o quiere empezar de nuevo.",
      "hint": "Usa reset_flow_context solo cuando el cliente indique claramente que quiere cambiar el pedido completo, empezar de cero, cancelar el pedido anterior o hacer otro pedido independiente. Si habia resumen o link pendiente y el cliente abandona ese pedido, usa checkout_action=abandon. Para agregar o quitar productos del pedido actual, usa modify_current_order.",
      "allowedTools": [
        "reset_flow_context",
        "set_fact"
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
      "key": "occasion",
      "role": "order.occasion",
      "label": "ocasion del vino",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "captureMode": "eager",
      "aliases": [
        "regalo",
        "compartir",
        "celebracion",
        "ocasion",
        "detalle"
      ],
      "retentionDays": 1
    },
    {
      "key": "order_finalized",
      "role": "order.finalized",
      "label": "cliente finalizo el carrito",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "captureMode": "onDemand",
      "aliases": [
        "finalizar",
        "cerrar_pedido",
        "no_agregar_mas",
        "nada_mas",
        "listo"
      ],
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
      "captureMode": "eager",
      "aliases": [
        "regalo",
        "tula",
        "empaque",
        "detalle"
      ],
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
      "captureMode": "eager",
      "aliases": [
        "ciudad",
        "municipio",
        "destino"
      ],
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
      "captureMode": "eager",
      "aliases": [
        "direccion",
        "domicilio",
        "barrio",
        "direccion de entrega"
      ],
      "retentionDays": 1
    },
    {
      "key": "delivery_phone",
      "role": "customer.phone",
      "label": "celular de entrega",
      "type": "phone",
      "required": true,
      "source": "user",
      "scope": "customer",
      "captureMode": "eager",
      "aliases": [
        "telefono",
        "celular",
        "whatsapp",
        "numero",
        "contacto"
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
      "captureMode": "eager",
      "aliases": [
        "nombre",
        "cliente",
        "recibe",
        "destinatario",
        "nombre de quien recibe"
      ]
    },
    {
      "key": "payment_method",
      "role": "payment.method",
      "label": "metodo de pago",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "captureMode": "eager",
      "aliases": [
        "efectivo",
        "transferencia"
      ],
      "retentionDays": 1
    },
    {
      "key": "order_checkout_presented",
      "role": "order.checkout_presented",
      "label": "resumen presentado",
      "type": "string",
      "required": false,
      "source": "system",
      "scope": "request",
      "retentionDays": 1
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
  "guards": {
    "capability:commerce.order_create": {
      "requires": [
        "verification:checkout_no_payment_prepared",
        "state:no_pending_checkout"
      ]
    }
  },
  "enabledTools": [
    "set_fact",
    "search_products",
    "add_order_item",
    "remove_order_item",
    "update_order_item_quantity",
    "get_order_draft",
    "prepare_order_checkout",
    "verify_payment",
    "create_order",
    "send_message_sequence",
    "reset_flow_context",
    "escalate_to_human"
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
      ],
      "killSwitchPhrases": [
        "quiero hablar con un humano",
        "quiero hablar con una persona",
        "agente real",
        "operador",
        "hablar con alguien",
        "hablar con ustedes",
        "asesor humano",
        "estoy muy molest",
        "queja formal",
        "voy a demandar",
        "soy distribuidor",
        "soy mayorista",
        "pedido mayorista",
        "compra mayorista",
        "quiero revender"
      ]
    },
    "external": {
      "enabled": true,
      "events": {
        "order_created": {
          "enabled": true,
          "contactType": "delivery",
          "pickupAddress": "Calle 16 # 9-35, Centro, Valledupar",
          "attemptTimeoutMinutes": 15,
          "attemptCodePrefix": "PED",
          "sendMessageSequence": "delivery_request",
          "attemptSentNotificationEvent": "delivery_requested",
          "acceptedNotificationEvent": "delivery_confirmed",
          "exhaustedNotificationEvent": "delivery_unavailable",
          "contacts": [
            {
              "businessInboundContactId": "E2EE3BA9-E6BF-43E2-8C1A-560CB724688B",
              "priority": 1,
              "retryEnabled": true
            },
            {
              "businessInboundContactId": "E3EE3BA9-E6BF-43E2-8C1A-560CB724688B",
              "priority": 2,
              "retryEnabled": true
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
  }
}';



IF ISJSON(@SettingsJson) <> 1
BEGIN
    THROW 51000, 'SeedSolorzanoAgentConfiguration: SettingsJson invalido.', 1;
END

UPDATE dbo.Agents
SET SettingsJson         = @SettingsJson,
    SystemPromptMarkdown = @SystemPrompt,
    Model                = N'gpt-4.1-mini',
    Temperature          = 0.68,
    MaxToolIterations    = 8,
    Description          = N'Asesora comercial Vinos Artesanales Solorzano: venta por WhatsApp con pedido, envio y pago.',
    IsActive             = 1,
    UpdatedAt            = GETUTCDATE()
WHERE AgentId = @AgentId;

PRINT N'SeedSolorzanoAgentConfiguration: Camila reconfigurada para negocio ' + CAST(@BusinessId AS NVARCHAR(36));
GO
