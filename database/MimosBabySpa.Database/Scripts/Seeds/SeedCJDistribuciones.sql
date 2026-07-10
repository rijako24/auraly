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
DECLARE @LocalCommerceConnectionId UNIQUEIDENTIFIER = 'C1D15A00-0000-0000-0000-000000000030';
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
        @LocalCommerceConnectionId AS IntegrationConnectionId,
        @BusinessId AS BusinessId,
        CAST(1 AS INT) AS ConnectionType,
        CAST(0 AS INT) AS Provider,
        CAST(0 AS INT) AS Capability,
        N'Comercio local CJ Distribuciones' AS [Name],
        N'local' AS AccountIdentifier,
        N'{"currency":"COP","manageStock":false}' AS SettingsJson,
        CAST(NULL AS NVARCHAR(MAX)) AS SecretsJson,
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
    "provider": "Local"
  },
  "operatingHours": {
    "enforce": true
  },
  "persona": "Eres el asistente comercial de CJ Distribuciones por WhatsApp. Atiendes pedidos de alimentos y productos de consumo para hogares, tiendas, minimercados, restaurantes, comidas rapidas y distribuidores. Hablas en espanol con tono claro, amable y practico. Guias la compra sin obligar al cliente a navegar menus y usas el catalogo como fuente de verdad.",
  "policies": "## REGLAS DEL FLUJO CJ DISTRIBUCIONES\n\n- En conversacion nueva, si no existe customer_name confiable y el cliente no lo informo en el mensaje actual, saluda: Hola! Bienvenido a CJ Distribuciones. Con gusto te ayudo a realizar tu pedido. Me indicas tu nombre o el nombre de tu establecimiento?\n- Guarda el nombre con set_fact y no lo vuelvas a pedir cuando ya exista, lo haya informado en el turno actual o sea cliente recurrente identificado.\n- Despues del nombre, si falta customer_type, pregunta por el perfil con las opciones: A. Hogar, B. Tienda o minimercado, C. Restaurante, D. Comida rapida, E. Distribuidor. Acepta respuestas naturales: ama de casa o para mi casa = Hogar; tienda o minimercado = TiendaMinimercado; restaurante = Restaurante; hamburguesas, perros o comidas rapidas = ComidaRapida; distribuidor, mayorista o al por mayor = Distribuidor. Si el cliente corrige el perfil despues, actualiza el fact.\n- Una vez identificado el perfil, invita a escribir directamente productos, busquedas, precios, disponibilidad, catalogo, receta, surtido o continuacion de pedido. No obligues a navegar un menu.\n- Para catalogo, precios, disponibilidad, referencias, listas de productos y recomendaciones comerciales, usa solo productos oficiales devueltos por search_products en el turno vigente. Nunca inventes presentaciones, precios, disponibilidad, descuentos ni condiciones por perfil.\n- Cuando el cliente escriba una lista, extrae cada producto y cantidad, busca referencias reales con search_products, resuelve primero coincidencias exactas y muestra opciones solo si hay ambiguedad. Agrega al carrito unicamente productos suficientemente identificados o confirmados.\n- Nunca respondas con placeholders como: se muestran referencias, presentacion y precio. Muestra resultados reales devueltos por la tool o explica claramente que no encontraste coincidencia.\n- Para recetas o preparaciones, puedes razonar ingredientes o complementos razonables, pero debes buscarlos en catalogo antes de ofrecerlos. Recomienda solo productos realmente encontrados. Si un complemento normal no aparece, dilo como no encontrado en el catalogo actual. No agregues recomendaciones sin aprobacion.\n- Usa el perfil como contexto comercial, no como restriccion rigida. Hogar: presentaciones familiares, cantidades moderadas e ideas practicas. TiendaMinimercado: rotacion, reventa, unidades, paquetes o cajas. Restaurante: rendimiento, institucional y consistencia. ComidaRapida: hamburguesas, perros, fritos, salsas, quesos, carnes frias y desechables si existen. Distribuidor: mayor volumen, cajas, paquetes o bultos, sin inventar descuentos.\n- Despues de agregar productos puedes hacer una sola recomendacion complementaria breve, relevante y basada en catalogo. No envies listas extensas, no repitas la misma recomendacion y no recomiendes productos sin relacion.\n- Si el cliente dice eso es todo, dame el total, no quiero agregar mas, cuanto seria, pasame la factura o equivalente, registra order_finalized=true y pasa inmediatamente al resumen del carrito. No sigas vendiendo.\n- El resumen, precios, subtotales, descuentos, impuestos, envio y total final deben venir del motor o las tools. No calcules mentalmente cuando exista workflow o tool para hacerlo.\n- Despues de que el cliente apruebe el carrito, pregunta si prefiere recogida o domicilio. Para recogida usa la direccion configurada de CJ Distribuciones o el punto de recogida disponible; para domicilio solicita solo datos faltantes y confiables.\n- Para pago, muestra solo los metodos configurados actualmente: efectivo al recibir y transferencia manual. Si selecciona transferencia, usa el workflow actual, informa el valor exacto y no afirmes pago recibido solo porque envie una imagen salvo validacion del sistema.",
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
      "conversationGuidance": "Consulta el pedido actual si hay duda. Para agregar productos nuevos, busca productos oficiales antes de agregar. Si cambia cantidades, usa update_order_item_quantity. Si el cambio afecta un resumen ya presentado, limpia o actualiza los facts de cierre necesarios y vuelve a mostrar el carrito actualizado.",
      "allowedActions": [
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
      "id": "human_handoff",
      "priority": 80,
      "goal": "Escalar a humano cuando el cliente lo pida, haya queja, caso mayorista especial o solicitud fuera del alcance.",
      "conversationGuidance": "Responde breve y cordial antes de escalar. Para distribuidores con negociacion especial, toma nombre, negocio y ciudad si los entregan, sin inventar condiciones comerciales.",
      "allowedActions": [
        "escalate_to_human",
        "set_fact"
      ]
    }
  ],
  "factSchema": [
    { "key": "customer_name", "role": "customer.name", "label": "nombre del cliente o establecimiento", "type": "string", "required": true, "source": "user", "scope": "customer", "captureMode": "eager", "aliases": ["nombre", "cliente", "establecimiento", "negocio", "recibe", "destinatario"] },
    { "key": "customer_type", "role": "customer.type", "label": "perfil del cliente", "type": "string", "required": true, "source": "user", "scope": "customer", "captureMode": "eager", "aliases": ["hogar", "ama de casa", "casa", "tienda", "minimercado", "restaurante", "comida rapida", "hamburguesas", "perros", "distribuidor", "mayorista", "al por mayor"] },
    { "key": "order_finalized", "role": "order.finalized", "label": "cliente finalizo el carrito", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "onDemand", "aliases": ["finalizar", "listo", "eso es todo", "dame el total", "cuanto seria", "pasame la factura", "nada mas", "no agregar mas"], "retentionDays": 1 },
    { "key": "cart_review_confirmed", "role": "order.cart_review_confirmed", "label": "carrito aprobado por el cliente", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "onDemand", "aliases": ["correcto", "asi esta bien", "confirmo", "esta bien", "aprobado"], "retentionDays": 1 },
    { "key": "delivery_method", "role": "shipping.method", "label": "modalidad de entrega", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "onDemand", "aliases": ["recogida", "recoger", "retiro", "domicilio", "entrega"], "retentionDays": 1 },
    { "key": "city", "role": "shipping.city", "label": "ciudad de entrega", "type": "string", "required": true, "source": "system", "defaultValue": "Valledupar", "scope": "request", "captureMode": "eager", "aliases": ["ciudad", "municipio", "destino"], "retentionDays": 1 },
    { "key": "delivery_address", "role": "shipping.address", "label": "direccion de entrega o recogida", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "onDemand", "aliases": ["direccion", "domicilio", "barrio", "referencia", "direccion de entrega", "punto de recogida"], "retentionDays": 1 },
    { "key": "delivery_phone", "role": "customer.phone", "label": "celular de entrega", "type": "phone", "required": true, "source": "user", "scope": "customer", "captureMode": "onDemand", "aliases": ["telefono", "celular", "whatsapp", "numero", "contacto"] },
    { "key": "payment_method", "role": "payment.method", "label": "metodo de pago", "type": "string", "required": true, "source": "user", "scope": "request", "captureMode": "onDemand", "aliases": ["efectivo", "contraentrega", "transferencia", "nequi", "bancolombia"], "retentionDays": 1 },
    { "key": "order_checkout_presented", "role": "order.checkout_presented", "label": "resumen final presentado", "type": "string", "required": false, "source": "system", "scope": "request", "retentionDays": 1 }
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
            "template": "order_checkout_manual_transfer"
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
    "order_checkout_manual_transfer": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nMetodo de pago: transferencia manual\n\nConfirmas tu pedido con esta informacion? Al confirmarlo, el equipo te indicara los datos de pago."
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
          "conversationGuidance": "Si falta customer_name y el cliente no lo informo en el mensaje actual, saluda exactamente: Hola! Bienvenido a CJ Distribuciones. Con gusto te ayudo a realizar tu pedido. Me indicas tu nombre o el nombre de tu establecimiento? Si ya lo dijo, registralo con set_fact y continua sin volver a pedirlo.",
          "allowedActions": ["set_fact"],
          "collect": ["customer_name"]
        },
        {
          "id": "customer_type",
          "name": "Perfil del cliente",
          "goal": "Clasificar el perfil comercial como Hogar, TiendaMinimercado, Restaurante, ComidaRapida o Distribuidor.",
          "advanceWhenFacts": ["customer_type"],
          "conversationGuidance": "Si falta customer_type, pregunta: Mucho gusto, {customer_name}. Para brindarte informacion y recomendaciones mas adecuadas, cual de estas opciones describe mejor tu perfil? A. Hogar B. Tienda o minimercado C. Restaurante D. Comida rapida E. Distribuidor. Acepta respuestas naturales y registra el valor canonico. Si el cliente corrige el perfil posteriormente, actualizalo.",
          "allowedActions": ["set_fact"],
          "collect": ["customer_type"]
        },
        {
          "id": "product_selection",
          "name": "Productos, catalogo y recomendaciones",
          "goal": "Recibir pedidos abiertos, resolver productos reales del catalogo, recomendar de forma controlada y construir el carrito hasta que el cliente finalice.",
          "advanceWhenFacts": ["order_finalized"],
          "conversationGuidance": "Cuando customer_name y customer_type existan, responde: Perfecto, {customer_name}. Puedes escribirme directamente los productos que necesitas o contarme que deseas preparar y te ayudo a encontrar opciones en nuestro catalogo. Usa search_products para listas directas, busquedas, catalogo, precios, disponibilidad, surtido y complementos. Para listas, busca cada producto y cantidad, muestra referencias reales y agrega solo coincidencias claras o confirmadas. Si hay ambiguedad, pide la referencia preferida usando candidatos reales. Para recetas o preparaciones, identifica ingredientes razonables, buscalos en catalogo y recomienda solo encontrados; puedes usar search_web_recipes como apoyo de idea cuando el cliente pida receta explicitamente, pero el catalogo manda para vender. Despues de agregar productos, puedes hacer una sola venta complementaria breve basada en catalogo y relacionada con lo pedido. Si el cliente indica que termino o pide total/factura, registra order_finalized=true de inmediato.",
          "allowedActions": [
            "search_products",
            "search_web_recipes",
            "add_order_item",
            "get_order_draft",
            "set_fact"
          ],
          "collect": ["order_finalized"],
          "entryActions": [
            {
              "tool": "search_products",
              "arguments": {
                "query": "{{user.message}}",
                "limit": 10
              },
              "when": {
                "requiredFacts": ["customer_name", "customer_type"]
              }
            }
          ]
        },
        {
          "id": "cart_review",
          "name": "Resumen inicial del pedido",
          "goal": "Mostrar el carrito con productos y subtotales disponibles antes de pedir entrega o pago.",
          "advanceWhenFacts": ["cart_review_confirmed"],
          "conversationGuidance": "Cuando order_finalized=true y existan items, usa get_order_draft para mostrar: Tu pedido queda asi, con cada producto, presentacion, cantidad y subtotal si la tool lo entrega. Muestra el total calculado por el sistema si esta disponible. Pregunta: Esta correcto o deseas modificar algo? Si confirma, registra cart_review_confirmed=true. Si modifica, aplica cambios con las tools de carrito y vuelve a mostrar el resumen.",
          "allowedActions": ["get_order_draft", "set_fact", "search_products", "add_order_item", "remove_order_item", "update_order_item_quantity"],
          "collect": ["cart_review_confirmed"]
        },
        {
          "id": "order_data",
          "name": "Entrega",
          "goal": "Definir recogida o domicilio y obtener solo los datos faltantes requeridos por el checkout.",
          "advanceWhenFacts": ["delivery_method", "city", "delivery_address", "delivery_phone", "customer_name"],
          "reentryOnFactChanged": ["delivery_method", "city", "delivery_address", "delivery_phone", "customer_name"],
          "conversationGuidance": "Despues de aprobar el carrito pregunta: Prefieres recoger tu pedido o recibirlo a domicilio? Si elige recogida, registra delivery_method=recogida y usa como delivery_address el punto de recogida configurado o Punto de recogida CJ Distribuciones - Valledupar, Cesar. Si elige domicilio, registra delivery_method=domicilio y solicita solo datos faltantes: direccion, barrio o referencia cuando aplique, telefono si no existe y nombre del receptor si falta. No pidas datos confiables ya disponibles. No pidas ciudad si ya existe por defecto; usa Valledupar salvo que el cliente indique otra ciudad.",
          "allowedActions": ["set_fact"],
          "collect": ["delivery_method", "city", "delivery_address", "delivery_phone", "customer_name"]
        },
        {
          "id": "payment_method",
          "name": "Metodo de pago",
          "goal": "Elegir uno de los metodos de pago configurados para CJ Distribuciones.",
          "advanceWhenFacts": ["payment_method"],
          "conversationGuidance": "Cuando la modalidad de entrega y datos requeridos esten completos, pregunta: Como deseas realizar el pago? Opciones configuradas: transferencia o efectivo. Registra payment_method=efectivo o payment_method=transferencia segun responda. No menciones metodos no configurados.",
          "allowedActions": ["set_fact", "get_order_draft"],
          "collect": ["payment_method"]
        },
        {
          "id": "summary",
          "name": "Resumen final del pedido",
          "goal": "Preparar y mostrar el resumen oficial con entrega, pago y total final del motor.",
          "advanceWhenFacts": ["order_checkout_presented"],
          "reentryOnFactChanged": ["order_finalized", "cart_review_confirmed", "delivery_method", "city", "delivery_address", "delivery_phone", "customer_name", "payment_method"],
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
          ],
          "conversationGuidance": "Cuando ya existan items, carrito aprobado, entrega y metodo de pago, llama prepare_order_checkout una sola vez. Muestra el resumen renderizado por la tool y pide confirmacion verbal. Para transferencia, informa solo lo que entregue el workflow actual y no confirmes pago recibido sin validacion del sistema. Si falla por configuracion no recuperable, escala a humano.",
          "allowedActions": ["prepare_order_checkout", "get_order_draft", "escalate_to_human"],
          "collect": ["order_checkout_presented"],
          "entryActions": [
            {
              "tool": "prepare_order_checkout",
              "arguments": {},
              "when": {
                "requiredFacts": ["cart_review_confirmed", "delivery_method", "city", "delivery_address", "delivery_phone", "customer_name", "payment_method"],
                "missingFacts": ["order_checkout_presented"]
              }
            }
          ]
        },
        {
          "id": "order_confirmation",
          "name": "Confirmacion del pedido",
          "goal": "Crear el pedido despues de confirmacion del cliente.",
          "advanceWhenFacts": [],
          "conversationGuidance": "Si el cliente confirma claramente el resumen final, crea el pedido con customer_confirmed=true, customer_name, customer_phone y delivery_address; despues envia la secuencia order_created_customer. Si corrige datos, metodo de pago o carrito, aplica el cambio y presenta resumen actualizado. No afirmes pago recibido solo por una imagen o comprobante si el workflow no lo valida.",
          "allowedActions": ["create_order", "send_message_sequence", "get_order_draft", "set_fact", "escalate_to_human"],
          "collect": []
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

PRINT N'SeedCJDistribuciones: negocio, catalogo y agente configurados.';

GO



