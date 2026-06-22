-- =============================================================================
-- SeedSolorzanoAgentConfiguration.sql
--
-- Configuracion completa del agente Camila (Vinos Artesanales Solorzano) para
-- el motor agentic actual. Idempotente.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';
DECLARE @AgentId    UNIQUEIDENTIFIER = 'B0EE3BA9-E6BF-43E2-8C1A-560CB724688B';
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

DECLARE @MigratedProducts TABLE
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

INSERT INTO @MigratedProducts
    (ProductId, Sku, [Name], [Description], CategoryName, UnitPrice, Currency, StockQuantity, DisplayOrder)
SELECT
    NEWID(),
    LEFT(NULLIF(LTRIM(RTRIM(s.ServiceName)), N''), 100),
    s.ServiceName,
    NULLIF(s.Description, N''),
    COALESCE(sc.Name, N'Vinos artesanales'),
    s.Price,
    N'COP',
    NULL,
    ROW_NUMBER() OVER (ORDER BY s.ServiceName)
FROM dbo.Services s
LEFT JOIN dbo.ServiceCategories sc ON sc.ServiceCategoryId = s.CategoryId
WHERE s.BusinessId = @BusinessId
  AND s.IsActive = 1
  AND (
      s.ServiceName LIKE N'%Vino%'
      OR s.ServiceName LIKE N'%Promo%'
      OR sc.Name LIKE N'%Vino%'
      OR sc.Name LIKE N'%Promo%'
  )
  AND s.ServiceName NOT LIKE N'%Foto%';

MERGE dbo.Products AS target
USING @MigratedProducts AS source
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

UPDATE dbo.Products
SET IntegrationConnectionId = @LocalCommerceConnectionId,
    IsActive = 1,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @BusinessId
  AND Source = 0
  AND (
      [Name] LIKE N'%Vino%'
      OR [Name] LIKE N'%Promo%'
      OR CategoryName LIKE N'%Vino%'
      OR CategoryName LIKE N'%Promo%'
  )
  AND [Name] NOT LIKE N'%Foto%';

DELETE FROM dbo.Products
WHERE BusinessId = @BusinessId
  AND Source = 0
  AND (
      [Name] LIKE N'%Foto%'
      OR CategoryName = N'Plan'
      OR (
          [Name] NOT LIKE N'%Vino%'
          AND [Name] NOT LIKE N'%Promo%'
          AND ISNULL(CategoryName, N'') NOT LIKE N'%Vino%'
          AND ISNULL(CategoryName, N'') NOT LIKE N'%Promo%'
      )
  );

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
  "persona": "Eres Camila, asesora comercial de Vinos Artesanales Solorzano por WhatsApp. Atiendes en espanol con tono humano, cercano y confiable, guiando la compra sin presion.\n\nResponde claro y breve. Para datos, opciones, resumen, envio o pago, usa listas cortas con campos claros.",
  "policies": "## PRODUCTO\n\n- Ninguno de los vinos artesanales Solorzano es elaborado a base de uva.\n- Todos los vinos artesanales Solorzano tienen 12 grados de alcohol.\n\n## CONVERSACION\n\n- Haz una sola pregunta accionable por turno. No combines seleccion de producto, variante/tamano, cantidad, datos de envio o pago en la misma pregunta.\n- Si faltan varios datos, pregunta solo el primero necesario para avanzar en este orden: producto, variante/tamano, cantidad, agregar mas, datos de envio, pago.\n- Para carrito o pedido, la prioridad es resolver agregar, quitar, reducir, cambiar productos o mostrar opciones antes de pedir datos de envio.\n- Cuando muestres opciones, cierra con una sola pregunta para elegir una opcion; cuando ya haya producto elegido, pregunta solo la cantidad.",
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
  ],
  "messageSequences": {
    "order_paid_customer": {
      "messages": [
        {
          "body": "Gracias por tu compra, {customer_name}. Recibimos el pago del pedido {order_number} por ${total} {currency}. Ya estamos coordinando el domicilio y te avisaremos si necesitamos algo adicional."
        }
      ]
    },
    "delivery_request": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "delivery_request",
          "language": "es_CO",
          "bodyParameters": ["{business_name}", "{attempt_code}", "{order_number}", "{customer_name}", "{customer_phone}", "{city}", "{delivery_address}", "{items}", "{total}", "{currency}"],
          "buttons": [
            { "id": "external_escalation:accept:{external_escalation_id}", "title": "Aceptar" },
            { "id": "external_escalation:decline:{external_escalation_id}", "title": "No tomar" }
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
          "bodyParameters": ["{order_number}", "{customer_name}", "{customer_phone}", "{city}", "{delivery_address}", "{items}", "{total}", "{currency}"]
        }
      ]
    },
    "delivery_requested": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "delivery_requested",
          "language": "es_CO",
          "bodyParameters": ["{order_number}", "{attempt_code}", "{contact_name}", "{contact_phone}", "{city}", "{delivery_address}", "{total}", "{currency}"]
        }
      ]
    },
    "delivery_confirmed": {
      "messages": [
        {
          "type": "whatsapp_template",
          "templateName": "delivery_confirmed",
          "language": "es_CO",
          "bodyParameters": ["{order_number}", "{attempt_code}", "{contact_name}", "{contact_phone}", "{customer_name}", "{customer_phone}", "{delivery_address}"]
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
        "goal": "Entender si el vino es para regalo o para compartir, recomendar opciones disponibles y construir el carrito hasta que el cliente finalice la compra.",
        "hint": "Si el mensaje del cliente es solo un saludo, saluda breve, presentate y pregunta si el vino lo busca para regalar o para compartir. En ese turno responde solo el saludo y la pregunta de ocasion. Cuando el cliente responda la ocasion, pida opciones, precios, promo o un producto, llama search_products antes de dar precios o disponibilidad y muestra 1 a 3 opciones activas en la misma respuesta. Para ocasion regalar/compartir usa una busqueda amplia: query vino, limit 3. Recomienda con lenguaje cercano y menciona la tula de regalo cuando encaje. Cierra con una invitacion suave a elegir una opcion. Cuando el cliente seleccione una opcion ya mostrada por numero, precio, tamano, sabor, nombre parcial o descripcion, resuelve cual producto corresponde en el ultimo resultado de search_products, conserva su product_id como producto elegido y continua con ese producto en los siguientes turnos. Si el cliente responde despues con una cantidad, interpreta esa cantidad para el producto elegido previamente y llama add_order_item con el product_id conservado y quantity. Si hay una sola coincidencia razonable por precio, numero, tamano, sabor, nombre parcial o descripcion, infierela y avanza. Si hay varias coincidencias razonables, haz una pregunta corta de aclaracion antes de agregar al carrito. La cantidad siempre debe estar expresada por el cliente antes de agregar al carrito. Cuando el producto este elegido y falte cantidad, pregunta cuantas unidades quiere llevar. Cuando ya tengas producto elegido y cantidad, llama add_order_item con el product_id exacto del producto elegido y quantity. Representa el carrito con el draft y sus items. Despues de agregar cada item, pregunta si quiere agregar algo mas a la compra. Cuando el cliente diga claramente que ya no quiere agregar mas y exista al menos un item en el carrito, llama set_fact order_finalized=true y avanza a datos de entrega. Si el carrito esta vacio, ayuda a elegir un producto primero. Si pregunta por algo fuera del catalogo activo, ofrece opciones activas.",
        "allowedTools": ["search_products", "add_order_item", "set_fact"],
        "advanceWhenFacts": ["order_finalized"]
      },
      {
        "id": "order_data",
        "name": "Datos del pedido",
        "goal": "Recoger ciudad, direccion, celular y nombre opcional para coordinar envio.",
        "hint": "Cuando falte city, delivery_address o delivery_phone, pide los datos faltantes juntos en una lista titulada Datos del pedido. Incluye estos campos cuando falten: Ciudad, Direccion de entrega, Celular de contacto, Nombre de quien recibe (opcional). Cuando el cliente responda, registra todos los datos que entregue con set_fact en el mismo turno. Si despues de registrar quedan datos requeridos pendientes, pide los faltantes juntos en lista.",
        "allowedTools": ["set_fact"],
        "advanceWhenFacts": ["city", "delivery_address", "delivery_phone"],
        "reentryOnFactChanged": ["city", "delivery_address", "delivery_phone"]
      },
      {
        "id": "summary",
        "name": "Resumen y total",
        "goal": "Mostrar resumen del pedido con total y link de pago por el 100%.",
        "hint": "Llama prepare_order_checkout cuando ya existan items y datos de entrega. La herramienta genera el resumen oficial, calcula envio segun checkout.modes.order.shipping y crea link de pago por el 100%. Muestra el resumen/link generado y espera confirmacion automatica del webhook. Si prepare_order_checkout falla por configuracion de pago, link de pago o un error no recuperable, responde breve y llama escalate_to_human en ese mismo turno con la razon y el ultimo mensaje relevante. No uses tools de reserva ni confirmes pedido antes del pago.",
        "allowedTools": ["prepare_order_checkout", "get_order_draft", "set_fact", "escalate_to_human"],
        "advanceWhenFacts": [],
        "reentryOnFactChanged": ["order_finalized", "city", "delivery_address"]
      },
      {
        "id": "payment",
        "name": "Metodo de pago",
        "goal": "Acompanar el pago online del pedido.",
        "hint": "El pedido se confirma solo cuando Wompi confirme el pago. Si el cliente dice transferencia, Bancolombia, Nequi, Daviplata, efectivo, contraentrega o que quiere pagar, acompana el pago con el link de Wompi ya generado; si aun no hay link vigente, llama prepare_order_checkout. Si pregunta por estado del pago, llama verify_payment. No entregues cuentas bancarias ni datos de pago manual. No llames create_order para cerrar pedidos pagados.",
        "allowedTools": ["prepare_order_checkout", "verify_payment", "get_order_draft", "escalate_to_human"],
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
      "allowedTools": ["escalate_to_human"]
    },
    {
      "id": "modify_current_order",
      "priority": 90,
      "goal": "Modificar el carrito actual cuando el cliente, despues de decir que no agregaba mas o despues de recibir el resumen/link, pida agregar otro producto.",
      "hint": "Si el cliente quiere agregar, quitar, reducir cantidades, cambiar productos o ver opciones para modificar el pedido actual, esta accion tiene prioridad sobre pedir datos de envio. Primero llama get_order_draft si necesitas identificar items existentes. Usa search_products para mostrar opciones o resolver productos, add_order_item para agregar, remove_order_item para quitar o ajustar cantidades, y despues muestra el carrito actualizado con get_order_draft. Si ya habia link de pago o ya estan los datos de entrega, vuelve a llamar prepare_order_checkout para generar un resumen/link actualizado; no le digas que use un link anterior.",
      "allowedTools": ["search_products", "add_order_item", "remove_order_item", "get_order_draft", "prepare_order_checkout"]
    },
    {
      "id": "restart_order",
      "priority": 70,
      "goal": "Reiniciar el pedido actual si el cliente cambia completamente de producto o quiere empezar de nuevo.",
      "hint": "Usa reset_flow_context solo cuando el cliente indique claramente que quiere cambiar el pedido completo. Conserva datos persistentes del cliente.",
      "allowedTools": ["reset_flow_context", "set_fact"]
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
      "aliases": ["regalo", "compartir", "celebracion", "ocasion", "detalle"]
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
      "aliases": ["finalizar", "cerrar_pedido", "no_agregar_mas", "nada_mas", "listo"]
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
      "aliases": ["regalo", "tula", "empaque", "detalle"]
    },
    {
      "key": "city",
      "role": "shipping.city",
      "label": "ciudad de entrega",
      "type": "string",
      "required": true,
      "source": "user",
      "scope": "request",
      "captureMode": "eager",
      "aliases": ["ciudad", "municipio", "destino"]
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
      "aliases": ["direccion", "domicilio", "barrio", "direccion de entrega"]
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
      "aliases": ["telefono", "celular", "whatsapp", "numero", "contacto"]
    },
    {
      "key": "customer_name",
      "role": "customer.name",
      "label": "nombre del cliente",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "customer",
      "captureMode": "eager",
      "aliases": ["nombre", "cliente", "recibe", "destinatario"]
    },
    {
      "key": "shipping_cost",
      "role": "shipping.cost",
      "label": "costo de envio",
      "type": "number",
      "required": false,
      "source": "system",
      "scope": "request"
    },
    {
      "key": "order_confirmed",
      "role": "order.confirmed",
      "label": "pedido confirmado",
      "type": "string",
      "required": false,
      "source": "user",
      "scope": "request",
      "aliases": ["confirmado", "confirmo", "si confirmo", "de acuerdo", "listo"]
    }
  ],
  "guards": {},
  "enabledTools": [
    "set_fact",
    "search_products",
    "add_order_item",
    "remove_order_item",
    "get_order_draft",
    "prepare_order_checkout",
    "verify_payment",
    "create_order",
    "reset_flow_context",
    "escalate_to_human"
  ],
  "escalation": {
    "contacts": ["+573205387559"]
  },
  "notifications": {
    "reservation_created": {
      "enabled": false,
      "recipients": [],
      "sendMessageSequence": null
    },
    "order_created": {
      "enabled": true,
      "recipients": ["+573205387559"],
      "sendMessageSequence": "order_created"
    },
    "delivery_requested": {
      "enabled": true,
      "recipients": ["+573205387559"],
      "sendMessageSequence": "delivery_requested"
    },
    "delivery_confirmed": {
      "enabled": true,
      "recipients": ["+573205387559"],
      "sendMessageSequence": "delivery_confirmed"
    }
  },
  "webhooks": {
    "wompi": {
      "order_paid": {
        "sendMessageSequence": "order_paid_customer"
      }
    }
  },
  "externalEscalations": {
    "enabled": true,
    "events": {
      "order_created": {
        "enabled": true,
        "strategy": "sequential",
        "attemptTimeoutMinutes": 5,
        "attemptCodePrefix": "PED",
        "sendMessageSequence": "delivery_request",
        "attemptSentNotificationEvent": "delivery_requested",
        "acceptedNotificationEvent": "delivery_confirmed",
        "contacts": [
          {
            "key": "domicilio_solorzano",
            "name": "Domicilio Solorzano",
            "role": "delivery",
            "phone": "+573205387559",
            "priority": 1,
            "inboundAgentId": "D0EE3BA9-E6BF-43E2-8C1A-560CB724688B"
          }
        ]
      }
    }
  },
  "checkout": {
    "currency": "COP",
    "modes": {
      "order": {
        "payment": { "type": "full", "percentage": 100 },
        "templateWithPayment": "order_checkout_with_payment",
        "confirmationOutcome": "order_paid",
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
    "order_checkout_with_payment": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{name}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total a pagar: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Nombre: {{customer_name}}\n{{/if}}\n\nPuedes pagar de forma segura en este enlace:\n{{link_url}}\n\nCuando el pago sea aprobado, te confirmaremos la compra automaticamente."
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



