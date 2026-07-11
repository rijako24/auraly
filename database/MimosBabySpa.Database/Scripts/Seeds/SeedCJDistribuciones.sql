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
  "model": "gpt-4.1-mini",
  "temperature": 0.62,
  "maxToolIterations": 8,
  "historyWindowSize": 24,
  "commerce": {
    "enabled": true,
    "provider": "Mantis"
  },
  "operatingHours": {
    "enforce": true,
    "outsideHours": {
      "guidance": "Responde de forma breve, cordial y cerrada. Explica que el negocio esta fuera de horario y que el proximo horario habil es {{next_operating_window}}. Adapta el mensaje a lo que dijo el cliente, pero no solicites datos, no prometas ejecutar gestiones, no abras catalogos y no termines con preguntas."
    }
  },
  "persona": "Eres el asistente comercial de CJ Distribuciones por WhatsApp. Atiendes pedidos de alimentos y productos de consumo para hogares, tiendas, minimercados, restaurantes, comidas rapidas y distribuidores. Hablas en espanol con tono claro, amable y practico. Guias la compra sin obligar al cliente a navegar menus y usas el catalogo como fuente de verdad.",
  "policies": "## PRESENTACION\n\nHola! Bienvenido a CJ Distribuciones. Con gusto te ayudo a realizar tu pedido. Me indicas tu nombre o el nombre de tu establecimiento?",
  "messageSequences": {
    "order_created_customer": {
      "messages": [
        {
          "body": "Gracias por tu pedido, {customer_name}. Lo recibimos correctamente y ya estamos coordinando la entrega."
        }
      ]
    }
  },
  "globalActions": [
    {
      "id": "modify_current_order",
      "priority": 90,
      "goal": "Modificar el carrito actual cuando el cliente pida agregar, quitar o cambiar productos o cantidades.",
      "conversationGuidance": "Consulta el pedido actual si hay duda. Para agregar productos nuevos, busca productos oficiales antes de agregar. Si cambia cantidades, usa update_order_item_quantity. Si el cambio afecta un resumen ya presentado, limpia o actualiza los facts de cierre necesarios y vuelve a mostrar el carrito actualizado."},
    {
      "id": "human_handoff",
      "priority": 80,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja, caso mayorista especial o solicitud fuera del alcance.",
      "conversationGuidance": "Responde breve y cordial antes de escalar. Para distribuidores con negociacion especial, toma nombre, negocio y ciudad si los entregan, sin inventar condiciones comerciales."}
  ],
  "factSchema": [
    { "key": "customer_name", "role": "customer.name", "label": "nombre del cliente o establecimiento", "type": "string", "required": true, "source": "user", "scope": "customer", "aliases": ["nombre", "cliente", "establecimiento", "negocio", "recibe", "destinatario"] },
    { "key": "customer_type", "role": "customer.type", "label": "perfil del cliente", "type": "string", "required": true, "source": "user", "scope": "customer", "aliases": ["hogar", "ama de casa", "casa", "tienda", "minimercado", "restaurante", "comida rapida", "hamburguesas", "perros", "distribuidor", "mayorista", "al por mayor"] },
    { "key": "order_finalized", "role": "order.finalized", "label": "cliente finalizo el carrito", "type": "boolean", "required": true, "source": "user", "scope": "request", "aliases": ["finalizar", "listo", "eso es todo", "solo eso", "dame el total", "cuanto seria", "pasame la factura", "nada mas", "no agregar mas"], "retentionDays": 1 },
    { "key": "cart_review_confirmed", "role": "order.cart_review_confirmed", "label": "carrito aprobado por el cliente", "type": "boolean", "required": true, "source": "user", "scope": "request", "aliases": ["correcto", "asi esta bien", "confirmo", "esta bien", "aprobado"], "retentionDays": 1 },
    { "key": "delivery_method", "role": "shipping.method", "label": "modalidad de entrega", "type": "string", "required": true, "source": "user", "scope": "request", "aliases": ["recogida", "recoger", "retiro", "domicilio", "entrega"], "retentionDays": 1 },
    { "key": "city", "role": "shipping.city", "label": "ciudad de entrega", "type": "string", "required": true, "source": "system", "defaultValue": "Valledupar", "scope": "request", "aliases": ["ciudad", "municipio", "destino"], "retentionDays": 1 },
    { "key": "delivery_address", "role": "shipping.address", "label": "direccion de entrega o recogida", "type": "string", "required": true, "source": "user", "scope": "request", "aliases": ["direccion", "domicilio", "barrio", "referencia", "direccion de entrega", "punto de recogida"], "retentionDays": 1 },
    { "key": "delivery_phone", "role": "customer.phone", "label": "celular de entrega", "type": "phone", "required": true, "source": "user", "scope": "customer", "aliases": ["telefono", "celular", "whatsapp", "numero", "contacto"] },
    { "key": "payment_method", "role": "payment.method", "label": "metodo de pago", "type": "string", "required": true, "source": "user", "scope": "request", "aliases": ["efectivo", "contraentrega", "transferencia", "nequi", "bancolombia"], "retentionDays": 1 },
    { "key": "order_checkout_presented", "role": "order.checkout_presented", "label": "resumen final presentado", "type": "boolean", "required": false, "source": "system", "scope": "request", "retentionDays": 1 },
    { "key": "system.recipe_catalog_queries", "role": "system.recipe_catalog_queries", "label": "consultas de catalogo derivadas de receta", "type": "json", "required": false, "source": "system", "scope": "request", "retentionDays": 1 },
    { "key": "customer_confirmed", "role": "confirmation.verbal", "label": "confirmacion verbal del pedido", "type": "boolean", "required": false, "source": "user", "scope": "request", "aliases": ["confirmo", "confirmo pedido", "si confirmo", "confirmado"], "dependsOn": ["order_checkout_presented", "cart_review_confirmed", "delivery_method", "city", "delivery_address", "delivery_phone", "customer_name", "payment_method"], "retentionDays": 1 }
  ],
  "guards": {
    "capability:commerce.order_create": {
      "requires": [
        "verification:checkout_no_payment_prepared",
        "state:no_pending_checkout",
        "flag:verbal_confirmation"
      ]
    }
  },
  "enabledTools": [
    "set_fact",
    "search_products",
    "search_web_recipes",
    "add_order_item",
    "remove_order_item",
    "update_order_item_quantity",
    "get_order_draft",
    "prepare_order_checkout",
    "create_order",
    "send_message_sequence",
    "reset_flow_context",
    "escalate_to_human"
  ],
  "notifications": {
    "order_created": {
      "enabled": false,
      "recipients": [],
      "sendMessageSequence": null
    }
  },
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
    "modes": {
      "order": {
        "paymentMethods": {
          "efectivo": {
            "label": "efectivo al recibir",
            "aliases": ["efectivo", "contraentrega"],
            "template": "order_checkout_no_payment"
          },
          "transferencia": {
            "label": "transferencia manual",
            "aliases": ["transferencia", "nequi", "bancolombia"],
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
  "templates": {
    "order_checkout_no_payment": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nMetodo de pago: efectivo al recibir\n\nConfirmas tu pedido con esta informacion?",
    "single_active_order_required": "Solo podemos gestionar un pedido activo por conversación. Termina el pedido actual antes de iniciar otro; no se aplicó ningún cambio.",
    "order_checkout_manual_transfer": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nMetodo de pago: transferencia manual\n\nTu pago queda pendiente de confirmacion manual. Un agente del equipo de CJ Distribuciones confirmara el pago; cuando se confirme, te notificaremos que el pedido fue creado."
  },
  "flows": [
    {
      "id": "order",
      "type": "primary",
      "routingGuidance": "Use this primary flow for CJ Distribuciones product orders, customer identification, profile classification, catalog-grounded recommendations, delivery data, payment method and order confirmation.",
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "customer_name",
          "name": "Identificacion del cliente",
          "goal": "Obtener el nombre del cliente o establecimiento antes de iniciar el pedido cuando no exista un nombre confiable.",
          "advanceWhenFacts": ["customer_name"],
          "conversationGuidance": "Si falta customer_name y el cliente no lo informo en el mensaje actual, saluda exactamente: Hola! Bienvenido a CJ Distribuciones. Con gusto te ayudo a realizar tu pedido. Me indicas tu nombre o el nombre de tu establecimiento? Si ya lo dijo, continúa sin volver a pedirlo; el motor registra el dato extraído.",
          "collect": ["customer_name"]
        },
        {
          "id": "customer_type",
          "name": "Perfil del cliente",
          "goal": "Clasificar el perfil comercial como Hogar, TiendaMinimercado, Restaurante, ComidaRapida o Distribuidor.",
          "advanceWhenFacts": ["customer_type"],
          "conversationGuidance": "Si falta customer_type, pregunta: Mucho gusto, {customer_name}. Para brindarte informacion y recomendaciones mas adecuadas, cual de estas opciones describe mejor tu perfil? A. Hogar B. Tienda o minimercado C. Restaurante D. Comida rapida E. Distribuidor. Acepta respuestas naturales y registra el valor canonico. Si el cliente corrige el perfil posteriormente, actualizalo.",
          "collect": ["customer_type"]
        },
        {
          "id": "product_selection",
          "name": "Productos, catalogo y recomendaciones",
          "goal": "Recibir pedidos abiertos, resolver productos reales del catalogo, recomendar de forma controlada y construir el carrito hasta que el cliente finalice.",
          "advanceWhenFacts": ["order_finalized"],
          "conversationGuidance": "Cuando customer_name y customer_type existan, responde: Perfecto, {customer_name}. Puedes escribirme directamente los productos que necesitas o contarme que deseas preparar y te ayudo a encontrar opciones en nuestro catalogo. Para listas directas, búsquedas, catálogo, precios, disponibilidad, surtido y complementos, responde únicamente con resultados oficiales producidos por las operaciones configuradas. Para listas, busca cada producto y cantidad, muestra referencias reales con nombre, presentacion y precio cuando el resultado vigente lo entregue; no muestres SKU, codigos internos ni unidades de stock en opciones normales; agrega solo coincidencias claras o confirmadas. Si hay ambiguedad, pide la referencia preferida usando candidatos reales. Si pides confirmacion sobre un producto pendiente despues de haber agregado otros, una respuesta afirmativa confirma solo ese producto pendiente; no vuelvas a agregar productos ya agregados salvo que el cliente pida mas unidades explicitamente. Para recetas o preparaciones, primero presenta el resultado de receta producido por la operación configurada y después los productos oficiales encontrados con las consultas de ingredientes derivadas, no con la frase completa del cliente. No respondas solo con links ni digas que si quiere luego buscas catalogo; en la misma respuesta muestra maximo dos ideas de receta y una seccion breve de ingredientes disponibles en catalogo con productos reales, presentacion y precio cuando la operación los entregue. No concluyas que no hay un ingrediente si no se busco como ingrediente separado. No muestres SKU, codigos internos ni unidades de stock salvo si el cliente pidio mas cantidad de la disponible, en cuyo caso informa la cantidad disponible y pregunta si desea incluir esa cantidad. Recomienda solo productos encontrados y pide confirmacion para agregarlos. Despues de agregar productos, puedes hacer una sola venta complementaria breve basada en catalogo y relacionada con lo pedido. Si el cliente indica que termino o pide total/factura, registra order_finalized=true de inmediato.",
          "collect": ["order_finalized"],
          "signals": [
            {
              "type": "recipe_request",
              "description": "El cliente pide ideas o recetas para preparar algo. El valor contiene únicamente el ingrediente o preparación principal expresada por el cliente.",
              "valueSchema": { "type": "string" }
            },
            {
              "type": "order_changes",
              "description": "Cambios explícitos solicitados por el cliente sobre productos del pedido. Extrae todos los productos y cantidades del mismo mensaje. productText contiene solo la frase que identifica el producto o presentación, sin repetir cantidad ni verbo de acción; quantity contiene la cantidad por separado. groupReference es null para un único pedido; si el cliente distribuye productos entre pedidos o direcciones distintas, contiene la dirección o referencia de grupo correspondiente en cada producto para que el motor rechace atómicamente el intento de pedidos simultáneos.",
              "valueSchema": {
                "type": "array",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "operation": { "type": "string", "enum": ["add", "remove", "set_quantity"] },
                    "productText": { "type": "string" },
                    "quantity": { "type": ["number", "null"] },
                    "groupReference": { "type": ["string", "null"] }
                  },
                  "required": ["operation", "productText", "quantity", "groupReference"]
                }
              }
            }
          ],
          "actions": [
            {
              "id": "search_recipe_request",
              "operation": "commerce.search_recipes",
              "trigger": "on_signal",
              "signal": "recipe_request",
              "arguments": { "ingredient": "{{signal.recipe_request.value}}", "query": "preparacion facil", "limit": 2 },
              "onOutcome": {
                "recipes.found": {
                  "effects": [
                    { "type": "facts.set_from_outcome", "bindings": { "system.recipe_catalog_queries": "catalog_search_queries" } }
                  ],
                  "response": { "guidance": "Presenta máximo dos ideas devueltas y luego muestra únicamente ingredientes encontrados en el catálogo oficial." }
                }
              }
            },
            {
              "id": "search_recipe_catalog_products",
              "operation": "commerce.search_products",
              "trigger": "when_ready",
              "condition": { "factPresent": "system.recipe_catalog_queries" },
              "arguments": { "queries": "{{fact.system.recipe_catalog_queries}}", "limit": 10 },
              "onOutcome": {
                "products.found": {
                  "effects": [ { "type": "facts.clear", "facts": ["system.recipe_catalog_queries"] } ],
                  "response": { "guidance": "Muestra solo productos reales devueltos por catálogo, con presentación y precio cuando estén disponibles." }
                }
              }
            },
            {
              "id": "apply_order_changes",
              "operation": "commerce.apply_order_changes",
              "trigger": "on_signal",
              "signal": "order_changes",
              "arguments": { "commands": "{{signal.order_changes.value}}" },
              "onOutcome": {
                "cart.applied": {
                  "response": { "guidance": "Confirma brevemente los cambios aplicados y continúa según el objetivo de la etapa." }
                },
                "cart.product_not_found": {
                  "response": { "mode": "ask_clarification", "guidance": "Indica que ese producto no se encontró y pide una descripción o referencia más precisa." }
                },
                "cart.product_ambiguous": {
                  "response": { "mode": "ask_clarification", "guidance": "Presenta únicamente los candidatos devueltos y pregunta cuál referencia desea." }
                },
                "cart.item_not_found_or_ambiguous": {
                  "response": { "mode": "ask_clarification", "guidance": "Aclara cuál producto existente del pedido desea modificar." }
                },
                "cart.conflicting_commands": {
                  "response": { "mode": "ask_clarification", "guidance": "Pide aclarar el cambio final para el producto repetido; no se aplicó ningún cambio del lote." }
                },
                "cart.multiple_orders": {
                  "response": { "template": "single_active_order_required" }
                }
              }
            }
          ]},
        {
          "id": "cart_review",
          "name": "Resumen inicial del pedido",
          "goal": "Mostrar el carrito con productos y subtotales disponibles antes de pedir entrega o pago.",
          "advanceWhenFacts": ["cart_review_confirmed"],
          "conversationGuidance": "Cuando order_finalized=true y existan items, usa get_order_draft para mostrar: Tu pedido queda asi, con cada producto, presentacion, cantidad y subtotal si el resultado vigente lo entrega. Muestra el total calculado por el sistema si esta disponible. Pregunta: Esta correcto o deseas modificar algo? Si confirma, registra cart_review_confirmed=true. Si modifica, aplica los cambios extraídos mediante la operación configurada y vuelve a mostrar el resumen.",
          "collect": ["cart_review_confirmed"],
          "signals": [
            {
              "type": "order_changes",
              "description": "Cambios explícitos solicitados por el cliente sobre productos del pedido. Extrae todos los productos y cantidades del mismo mensaje. productText contiene solo la frase que identifica el producto o presentación, sin repetir cantidad ni verbo de acción; quantity contiene la cantidad por separado. groupReference es null para un único pedido; si el cliente distribuye productos entre pedidos o direcciones distintas, contiene la dirección o referencia de grupo correspondiente en cada producto para que el motor rechace atómicamente el intento de pedidos simultáneos.",
              "valueSchema": {
                "type": "array",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "operation": { "type": "string", "enum": ["add", "remove", "set_quantity"] },
                    "productText": { "type": "string" },
                    "quantity": { "type": ["number", "null"] },
                    "groupReference": { "type": ["string", "null"] }
                  },
                  "required": ["operation", "productText", "quantity", "groupReference"]
                }
              }
            }
          ],
          "actions": [
            {
              "id": "show_current_order_draft",
              "operation": "commerce.get_order_draft",
              "trigger": "when_ready",
              "arguments": {},
              "onOutcome": {
                "order.draft_loaded": { "response": { "guidance": "Muestra los ítems, cantidades, subtotales y total devueltos, y pregunta si el pedido actual está correcto." } }
              }
            },
            {
              "id": "apply_order_changes",
              "operation": "commerce.apply_order_changes",
              "trigger": "on_signal",
              "signal": "order_changes",
              "arguments": { "commands": "{{signal.order_changes.value}}" },
              "onOutcome": {
                "cart.applied": {
                  "response": { "guidance": "Confirma brevemente los cambios aplicados y continúa según el objetivo de la etapa." }
                },
                "cart.product_not_found": {
                  "response": { "mode": "ask_clarification", "guidance": "Indica que ese producto no se encontró y pide una descripción o referencia más precisa." }
                },
                "cart.product_ambiguous": {
                  "response": { "mode": "ask_clarification", "guidance": "Presenta únicamente los candidatos devueltos y pregunta cuál referencia desea." }
                },
                "cart.item_not_found_or_ambiguous": {
                  "response": { "mode": "ask_clarification", "guidance": "Aclara cuál producto existente del pedido desea modificar." }
                },
                "cart.conflicting_commands": {
                  "response": { "mode": "ask_clarification", "guidance": "Pide aclarar el cambio final para el producto repetido; no se aplicó ningún cambio del lote." }
                },
                "cart.multiple_orders": {
                  "response": { "template": "single_active_order_required" }
                }
              }
            }
          ]
        },
        {
          "id": "order_data",
          "name": "Entrega",
          "goal": "Definir recogida o domicilio y obtener solo los datos faltantes requeridos por el checkout.",
          "advanceWhenFacts": ["delivery_method", "city", "delivery_address", "delivery_phone", "customer_name"],
          "reentryOnFactChanged": ["delivery_method", "city", "delivery_address", "delivery_phone", "customer_name"],
          "conversationGuidance": "Despues de aprobar el carrito pregunta: Prefieres recoger tu pedido o recibirlo a domicilio? Si elige recogida, registra delivery_method=recogida y usa como delivery_address el punto de recogida configurado o Punto de recogida CJ Distribuciones - Valledupar, Cesar. Si elige domicilio, registra delivery_method=domicilio y solicita solo datos faltantes: direccion, barrio o referencia cuando aplique, telefono si no existe y nombre del receptor si falta. No pidas datos confiables ya disponibles. No pidas ciudad si ya existe por defecto; usa Valledupar salvo que el cliente indique otra ciudad.",
          "collect": ["delivery_method", "city", "delivery_address", "delivery_phone", "customer_name"]
        },
        {
          "id": "payment_method",
          "name": "Metodo de pago",
          "goal": "Elegir uno de los metodos de pago configurados para CJ Distribuciones.",
          "advanceWhenFacts": ["payment_method"],
          "conversationGuidance": "Cuando la modalidad de entrega y datos requeridos esten completos, pregunta: Como deseas realizar el pago? Opciones configuradas: transferencia o efectivo. Registra payment_method=efectivo o payment_method=transferencia segun responda. No menciones metodos no configurados.",
          "collect": ["payment_method"]
        },
        {
          "id": "summary",
          "name": "Resumen final del pedido",
          "goal": "Preparar y mostrar el resumen oficial con entrega, pago y total final del motor.",
          "advanceWhenFacts": ["order_checkout_presented"],
          "reentryOnFactChanged": ["order_finalized", "cart_review_confirmed", "delivery_method", "city", "delivery_address", "delivery_phone", "customer_name", "payment_method"],
          "actions": [
            {
              "id": "prepare_order_checkout",
              "operation": "commerce.prepare_checkout",
              "trigger": "when_ready",
              "condition": {
                "all": [
                  { "factPresent": "cart_review_confirmed" },
                  { "factPresent": "delivery_method" },
                  { "factPresent": "city" },
                  { "factPresent": "delivery_address" },
                  { "factPresent": "delivery_phone" },
                  { "factPresent": "customer_name" },
                  { "factPresent": "payment_method" },
                  { "factMissing": "order_checkout_presented" }
                ]
              },
              "arguments": {},
              "onOutcome": {
                "order.checkout_ready": {
                  "effects": [ { "type": "fact.set", "fact": "order_checkout_presented", "value": true } ]
                },
                "order.checkout_payment_required": {
                  "effects": [ { "type": "fact.set", "fact": "order_checkout_presented", "value": true } ]
                },
                "order.checkout_pending_manual_payment": {
                  "effects": [ { "type": "fact.set", "fact": "order_checkout_presented", "value": true } ]
                }
              }
            }
          ],          "conversationGuidance": "Cuando ya existan items, carrito aprobado, entrega y metodo de pago, el motor prepara el checkout una sola vez. Si el metodo es efectivo, muestra el resumen autoritativo renderizado por el motor y pide confirmacion verbal. Si el metodo es transferencia, muestra el resumen autoritativo renderizado por el motor e informa que el pago queda pendiente de confirmacion manual por el equipo; no pidas comprobante, no pidas confirmacion adicional del pedido y no confirmes que el pedido fue creado. Si falla por configuracion no recuperable, escala a humano.",
          "collect": ["order_checkout_presented"]},
        {
          "id": "order_confirmation",
          "name": "Confirmacion del pedido",
          "goal": "Crear el pedido despues de confirmacion del cliente.",
          "advanceWhenFacts": ["customer_confirmed"],
          "actions": [
            {
              "id": "create_confirmed_cash_order",
              "operation": "commerce.create_order",
              "trigger": "when_ready",
              "condition": {
                "all": [
                  { "factEquals": { "key": "payment_method", "value": "efectivo" } },
                  { "factEquals": { "key": "customer_confirmed", "value": true } }
                ]
              },
              "arguments": { "customer_confirmed": "{{fact.customer_confirmed}}" },
              "onOutcome": {
                "order.created": {
                  "effects": [
                    { "type": "sequence.enqueue", "sequence": "order_created_customer" },
                    { "type": "request.complete" }
                  ],
                  "response": { "guidance": "Confirma el pedido únicamente con los datos autoritativos devueltos por la operación." }
                }
              }
            }
          ],          "conversationGuidance": "Si payment_method=transferencia, no pidas confirmacion verbal, no confirmes que el pedido fue creado y responde que el pago queda pendiente de confirmacion manual por el equipo de CJ Distribuciones; cuando el pago se confirme manualmente, el sistema notificara que el pedido fue creado. Si payment_method=efectivo y falta customer_confirmed, pide confirmacion verbal del resumen final y registrala solo cuando el cliente la entregue claramente. Con customer_confirmed=true y metodo efectivo, crea el pedido usando los facts vigentes y despues envia la secuencia order_created_customer. Si corrige datos, metodo de pago o carrito, aplica el cambio y presenta resumen actualizado. No afirmes pago recibido solo por una imagen o comprobante si el workflow no lo valida.",
          "collect": ["customer_confirmed"]
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
         SettingsJson, SystemPromptMarkdown, Model, Temperature, MaxToolIterations, CreatedAt)
    VALUES
        (@AgentId, @BusinessId, @AgentTypeId, N'Asistente CJ Distribuciones',
         N'Asistente comercial para pedidos, clasificacion de cliente y recetas web para hogar.',
         1, @SettingsJson, N'', N'gpt-4.1-mini', 0.62, 8, GETUTCDATE());
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
        SystemPromptMarkdown = N'',
        Model = N'gpt-4.1-mini',
        Temperature = 0.62,
        MaxToolIterations = 8,
        UpdatedAt = GETUTCDATE()
    WHERE AgentId = @AgentId;
END

PRINT N'SeedCJDistribuciones: negocio, Mantis y agente configurados.';

GO
