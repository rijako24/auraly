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
        N'{"baseUrl":"http://93.189.95.109:8080/MantisFiccCasalinsPruWeb/rest/","currency":"COP","requestTimeoutSeconds":120,"genericCustomer":{"llaveNit":"5702","llaveCliente":"1"},"catalog":{"searchEndpoint":"pwsConsultarArticuloCasalins","defaultPageSize":5,"maxPageSize":50},"order":{"createEndpoint":"pwsCrearPedidoCasalins","queryEndpoint":"pwsConsultarPedidoCasalins"}}' AS SettingsJson,
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

DECLARE @RecommendationRules TABLE
(
    ProductRecommendationRuleId UNIQUEIDENTIFIER NOT NULL,
    MatchType INT NOT NULL,
    SourceValue NVARCHAR(300) NOT NULL,
    RecommendedExternalProductId NVARCHAR(300) NOT NULL,
    RecommendedSku NVARCHAR(100) NULL,
    RecommendedSearchText NVARCHAR(300) NULL,
    RecommendationType INT NOT NULL,
    Priority INT NOT NULL,
    Reason NVARCHAR(500) NULL
);

INSERT INTO @RecommendationRules
    (ProductRecommendationRuleId, MatchType, SourceValue, RecommendedExternalProductId,
     RecommendedSku, RecommendedSearchText, RecommendationType, Priority, Reason)
VALUES
    ('C1D15A00-0000-0000-0000-000000000200', 0, N'PO28', N'CF127', N'CF127', N'tocineta', 0, 100, N'La tocineta es una buena opcion para complementar preparaciones con pechuga criolla.'),
    ('C1D15A00-0000-0000-0000-000000000201', 0, N'PO08', N'CF127', N'CF127', N'tocineta', 0, 100, N'La tocineta combina bien con preparaciones hechas con pechuga.'),
    ('C1D15A00-0000-0000-0000-000000000202', 0, N'PO39', N'CF127', N'CF127', N'tocineta', 0, 100, N'La tocineta combina bien con preparaciones hechas con pechuga.'),
    ('C1D15A00-0000-0000-0000-000000000203', 0, N'PO29', N'CF127', N'CF127', N'tocineta', 0, 100, N'La tocineta combina bien con preparaciones hechas con pechuga.'),
    ('C1D15A00-0000-0000-0000-000000000204', 0, N'PO36', N'SA30', N'SA30', N'tocineta', 0, 100, N'Esta salsa puede servirte para darle un sabor diferente a los trozos de pechuga.'),
    ('C1D15A00-0000-0000-0000-000000000205', 0, N'CF59', N'CG29', N'CG29', N'papa', 0, 100, N'Las papas a la francesa son un acompanamiento practico para la salchicha.'),
    ('C1D15A00-0000-0000-0000-000000000206', 1, N'CARNE DE POLLO', N'SA30', N'SA30', N'tocineta', 0, 50, N'Esta salsa es una opcion sencilla para acompanar productos de pollo.'),
    ('C1D15A00-0000-0000-0000-000000000207', 1, N'CARNE DE CERDO', N'CF127', N'CF127', N'tocineta', 0, 50, N'La tocineta puede complementar distintas preparaciones de cerdo.'),
    ('C1D15A00-0000-0000-0000-000000000208', 1, N'CARNES FRIAS', N'PA27', N'PA27', N'pan', 0, 50, N'El pan brioche es una opcion util para preparar hamburguesas, perros o sandwiches.'),
    ('C1D15A00-0000-0000-0000-000000000209', 1, N'PRODUCTOS CONGELADOS', N'AC06', N'AC06', N'aceite', 0, 50, N'El aceite puede servirte para preparar varios productos congelados.');

MERGE dbo.ProductRecommendationRules AS target
USING @RecommendationRules AS source
   ON target.BusinessId = @BusinessId
  AND target.ProductRecommendationRuleId = source.ProductRecommendationRuleId
WHEN MATCHED THEN
    UPDATE SET
        IntegrationConnectionId = @MantisCommerceConnectionId,
        MatchType = source.MatchType,
        SourceProductId = NULL,
        SourceValue = source.SourceValue,
        RecommendedProductId = NULL,
        RecommendedExternalProductId = source.RecommendedExternalProductId,
        RecommendedSku = source.RecommendedSku,
        RecommendedSearchText = source.RecommendedSearchText,
        RecommendationType = source.RecommendationType,
        Priority = source.Priority,
        Reason = source.Reason,
        IsActive = 1,
        StartsAtUtc = NULL,
        EndsAtUtc = NULL,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT
        (ProductRecommendationRuleId, BusinessId, IntegrationConnectionId, MatchType,
         SourceProductId, SourceValue, RecommendedProductId, RecommendedExternalProductId,
         RecommendedSku, RecommendationType, Priority, Reason, IsActive, StartsAtUtc,
         EndsAtUtc, CreatedAt)
    VALUES
        (source.ProductRecommendationRuleId, @BusinessId, @MantisCommerceConnectionId,
         source.MatchType, NULL, source.SourceValue, NULL, source.RecommendedExternalProductId,
         source.RecommendedSku, source.RecommendationType, source.Priority, source.Reason,
         1, NULL, NULL, GETUTCDATE());


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
    "provider": "Mantis",
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
  "persona": "Eres el asistente comercial de CJ Distribuciones por WhatsApp. Atiendes pedidos de alimentos y productos de consumo para hogares y negocios. Hablas en espanol de forma cercana, empatica, natural y servicial, como una persona atenta que acompana al cliente a armar su pedido. Usas parrafos cortos y espacios en blanco para que el mensaje sea facil de leer en WhatsApp. Evitas sonar como formulario, menu automatico o instruccion rigida. Puedes usar un emoji amable de manera ocasional, sin exagerar. El saludo inicial y el cierre son los momentos para usar el nombre del cliente; en los turnos intermedios respondes directamente. El catalogo y los resultados de las operaciones son la fuente de verdad comercial.",
  "policies": "## EXPERIENCIA CONVERSACIONAL\n\n- Responde primero a la intencion real de la persona y conserva la continuidad con el turno anterior.\n- Reconoce elecciones, avances o inquietudes de forma natural solo cuando aporte valor; varia las transiciones para mantener una conversacion fluida.\n- Usa el nombre con moderacion, principalmente en una apertura, un momento de tranquilidad o un cierre significativo.\n- Consulta la conversacion reciente para evitar repetir saludos, nombres, agradecimientos o la misma explicacion en turnos consecutivos.\n- Adapta el tono al mensaje recibido y manten una actitud humana, atenta, empatica y profesional.\n- Ante confusion, inconvenientes o incertidumbre, demuestra comprension y explica el siguiente paso con claridad.\n- En WhatsApp, usa mensajes breves, parrafos cortos y listas legibles cuando ayuden a entender opciones o resumenes.\n- Formula una sola pregunta enfocada cuando sea necesaria para avanzar.\n\n## PRESENTACION\n\n- Presentate como asistente de CJ Distribuciones con tono breve, amable y practico.\n- Reserva el nombre del cliente para el saludo inicial y el cierre; en los turnos intermedios responde directamente.\n- Presenta catalogos, precios, carrito, totales y estado del pedido exclusivamente desde resultados oficiales del turno.",
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
                    "cart_review_confirmed",
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
      "conversationGuidance": "Detecta catalog_query unicamente cuando el significado resuelto del mensaje solicita buscar mercancia comprable en el catalogo. Cada termino de queries debe ser una entrada valida para el buscador de productos e identificar un producto, categoria, ingrediente o preparacion que el cliente consulta, ya sea expresado en el mensaje vigente o resuelto desde una pregunta inmediatamente anterior que tambien era sobre productos. Si la pregunta pide recuperar o confirmar datos de entrega, direccion, recogida, pago, identidad, perfil, cliente u orden, emite cero catalog_query aunque use palabras como cual, tienes, disponible o registrado. Esta capacidad es transversal y puede ocurrir durante cualquier etapa, pero nunca funciona como respuesta generica a preguntas. Cuando la consulta nace porque el cliente rechaza semanticamente una referencia que ya esta en el carrito y pide alternativas, establece replacement_reference con la referencia original rechazada, sin depender de palabras exactas. Nunca respondas disponibilidad o nombres de productos desde conocimiento general.",
      "signal": {
        "type": "catalog_query",
        "description": "Consulta inequivoca sobre mercancia comprable del catalogo: existencia, opciones, referencias, precios, disponibilidad o recomendaciones, sin una instruccion explicita de agregar cantidades al pedido. El valor contiene terminos de producto concretos resueltos desde el mensaje vigente; replacement_reference identifica semanticamente la referencia vigente del carrito que el cliente rechazo y quiere sustituir. El contexto solo resuelve referencias comerciales que el cliente efectivamente consulta.",
        "valueSchema": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "queries": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "minItems": 1
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
            "replacement_reference": "{{signal.catalog_query.value.replacement_reference}}",
            "limit": 10
          },
          "onOutcome": {
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
      "key": "cart_review_confirmed",
      "role": "order.cart_review_confirmed",
      "label": "carrito aprobado por el cliente",
      "type": "boolean",
      "required": true,
      "source": "user",
      "scope": "request",
      "retentionDays": 1,
      "extractionGuidance": "Representa la aprobacion explicita del resumen vigente del carrito."
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
        "cart_review_confirmed",
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
    "guidance": "Escribe una sola bienvenida calida como primer parrafo: saluda, da la bienvenida a CJ Distribuciones y expresa que es un gusto saludarle. Si conoces el nombre del cliente, usalo una sola vez; si no lo conoces, no inventes ninguno. Puedes usar uno o dos emojis naturales. No digas ''aqui estoy para lo que necesites'' ni hagas preguntas en este primer parrafo. No menciones el tipo de cliente, ciudad, direccion, telefono, compras anteriores ni otros datos recordados. La continuacion, separada por una linea en blanco, debe seguir el objetivo de la etapa.",
    "allowQuestions": false
  },
  "failureResponses": {
    "llmUnavailable": "Lo siento, en este momento tengo un inconveniente temporal para procesar tu mensaje. Por favor, intenta nuevamente en unos minutos."
  },
  "templates": {
    "order_checkout_no_payment": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: {{customer_name}}\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: efectivo al recibir\n\nConfirmas tu pedido con esta informacion?",
    "order_checkout_card_terminal": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: {{customer_name}}\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: datafono al recibir\n\nLlevaremos el datafono para realizar el pago al momento de la entrega. Confirmas tu pedido con esta informacion?",
    "order_checkout_manual_transfer": "*Resumen de tu pedido*\n{{#each line_items}}\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\n{{/each}}\n- Envio: ${{shipping_cost}}\n- *Total: ${{total}} {{currency}}*\n\nEntrega:\n- Ciudad: {{city}}\n- Direccion: {{delivery_address}}\n- Celular: {{customer_phone}}\n{{#if customer_name}}\n- Cliente: {{customer_name}}\n{{/if}}\n{{#if delivery_recipient_name}}\n- Recibe: {{delivery_recipient_name}}\n{{/if}}\n\nMetodo de pago: transferencia manual\n\nTu pago queda pendiente de confirmacion manual. Un agente del equipo de CJ Distribuciones confirmara el pago; cuando se confirme, te notificaremos que el pedido fue creado.",
    "catalog_results": "Claro, encontre estas opciones para ti:\r\n\r\n*Productos disponibles*\r\n\r\n{{#each products}}\r\n- {{name}}: ${{unit_price}} {{currency}}\r\n{{/each}}\r\n\r\n{{#each recommendations}}\r\n\r\n*Tambien te puede servir*\r\n- {{name}}: ${{unit_price}} {{currency}}\r\n{{#if reason}}{{reason}}\r\n{{/if}}{{/each}}\r\n\r\nCual te interesa y cuantas unidades deseas agregar?",
    "catalog_no_results": "Por ahora no encontre {{#if search_text}}{{search_text}} disponibles{{else}}productos disponibles para esa busqueda{{/if}} en nuestro catalogo.\r\n\r\nSi quieres, puedo buscar una opcion parecida o ayudarte a elegir otro producto.",
    "known_facts": "Claro. Esto es lo que tengo registrado:\r\n\r\n{{#each facts}}\r\n- {{label}}: {{value}}\r\n{{/each}}",
    "known_facts_missing": "No tengo ese dato registrado todavia. Si quieres, puedes indicarmelo o actualizarlo.",
    "recipe_results": "Buena idea. Puedes inspirarte con estas preparaciones:\r\n\r\n*Ideas para preparar*\r\n{{#each results}}\r\n- {{Title}}\r\n  {{Url}}\r\n{{/each}}",
    "cart_snapshot": "Listo, ya actualice tu pedido 🙌\r\n\r\n*Pedido actual*\r\n\r\n{{#each items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\nQuieres agregar algo mas? Cuando termines, solo dime que eso es todo.",
    "cart_review": "Perfecto, revisemos juntos que todo este bien:\r\n\r\n*Resumen de tu pedido*\r\n\r\n{{#each items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\nLo ves correcto o quieres cambiar algo?",
    "product_ambiguity": "Quiero asegurarme de agregar la opcion correcta. Para {{product_text}} encontre:\r\n{{#each product_options}}\r\n- {{Name}}: ${{UnitPrice}} {{Currency}}\r\n{{/each}}\r\n\r\nCual prefieres? Conservare los demas productos de tu solicitud.",
    "insufficient_stock": "Puedo ayudarte con esa referencia, pero la cantidad solicitada supera el inventario disponible.\r\n\r\n- Producto: {{product_text}}\r\n- Solicitado en total: {{requested_quantity}}\r\n- Disponible: {{available_quantity}}\r\n\r\nPara este cambio, indica una cantidad de hasta {{maximum_command_quantity}}; los demas cambios del lote aun no se han aplicado."
  },
  "flows": [
    {
      "id": "order",
      "type": "primary",
      "routingGuidance": "Use this primary flow for CJ Distribuciones product orders, customer identification, profile classification, catalog-grounded recommendations, delivery data, payment method and order confirmation.",
      "stages": [
        {
          "id": "customer_name",
          "name": "Identificacion del cliente",
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
          "id": "cart_review",
          "name": "Transicion al cierre",
          "goal": "Continuar hacia entrega y pago sin mostrar un resumen intermedio.",
          "advanceWhenFacts": [
            "order_finalized"
          ],
          "conversationGuidance": "Cuando el cliente termine de agregar productos, avanza directamente a modalidad y datos de entrega. No muestres ni solicites confirmacion de un resumen intermedio; el unico resumen de cierre se presenta despues de completar entrega y pago.",
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
              "id": "show_current_order_draft",
              "operation": "commerce.get_order_draft",
              "trigger": "when_ready",
              "condition": {
                "factMissing": "order_finalized"
              },
              "arguments": {},
              "onOutcome": {
                "order.draft_loaded": {
                  "response": {
                    "guidance": "Muestra los Ã­tems, cantidades, subtotales y total devueltos, y pregunta si el pedido actual estÃ¡ correcto."
                  },
                  "effects": [
                    {
                      "type": "presentation.add",
                      "template": "cart_review",
                      "dataPath": "order",
                      "mode": "Exclusive",
                      "priority": "Required"
                    }
                  ]
                },
                "order.draft_empty": {
                  "response": {
                    "guidance": "Informa que el pedido vigente esta vacio y ayuda al cliente a elegir productos antes de continuar."
                  },
                  "effects": [
                    {
                      "type": "facts.clear",
                      "facts": [
                        "order_finalized",
                        "cart_review_confirmed"
                      ]
                    }
                  ]
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
                      "template": "cart_review",
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
          "goal": "Elegir uno de los metodos de pago configurados para CJ Distribuciones.",
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
                        "cart_review_confirmed",
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
                        "cart_review_confirmed",
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
          "conversationGuidance": "Si payment_method=transferencia, no pidas confirmacion verbal, no confirmes que el pedido fue creado y responde que el pago queda pendiente de confirmacion manual por el equipo de CJ Distribuciones; cuando el pago se confirme manualmente, el sistema notificara que el pedido fue creado. Si payment_method=efectivo o payment_method=datafono y falta customer_confirmed, pide confirmacion verbal del resumen final y registrala solo cuando el cliente la entregue claramente. Con customer_confirmed=true y metodo efectivo o datafono, crea el pedido usando los facts vigentes. Para datafono, recuerda que se llevara el dispositivo y no afirmes que el pago ya fue recibido. Despues de crear el pedido envia la secuencia order_created_customer. Si corrige datos, metodo de pago o carrito, aplica el cambio y presenta resumen actualizado. No afirmes pago recibido solo por una imagen o comprobante si el workflow no lo valida.",
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
    N'Listo, apliqué estos cambios al pedido:
{{#each applied_items}}
{{#if removed}}- Retiré {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} del carrito{{else}}- Agregué o actualicé {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}{{/if}}
{{/each}}

¿Deseas agregar algo mas?');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_on_request',
    N'Este es tu pedido actual:

{{#each items}}
- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}
{{/each}}

*Total actual: ${{total}} {{currency}}*

¿Deseas agregar o cambiar algo mas?');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, 'append $.globalActions', JSON_QUERY(N'{"id":"cart_review_request","priority":874,"goal":"Mostrar el carrito vigente cuando el cliente solicite verlo, sin mutarlo ni intentar resolver referencias pendientes.","conversationGuidance":"Emite cart_review_request ante cualquier solicitud de ver, revisar o saber como va el carrito o pedido actual. Es una consulta de solo lectura y nunca debe convertirse en order_changes.","signal":{"type":"cart_review_request","description":"Solicitud de solo lectura para presentar el carrito vigente.","valueSchema":{"type":"object","additionalProperties":false,"properties":{}}},"actions":[{"id":"show_current_cart","operation":"commerce.get_order_draft","trigger":"on_signal","signal":"cart_review_request","arguments":{},"onOutcome":{"order.draft_loaded":{"response":{"guidance":"Presenta el carrito vigente y pregunta si desea agregar o cambiar algo."},"effects":[{"type":"presentation.add","template":"cart_on_request","dataPath":"order","mode":"Exclusive","priority":"Required"}]},"order.draft_empty":{"response":{"guidance":"Indica brevemente que el carrito esta vacio y pregunta que desea agregar."}},"order_draft_missing":{"response":{"guidance":"Indica brevemente que aun no hay un carrito activo y pregunta que desea agregar."}}},"execution":{"idempotency":"none"}}]}'));
DECLARE @CartReviewGlobalActionIndex INT;
SELECT @CartReviewGlobalActionIndex = TRY_CONVERT(INT, [key])
FROM OPENJSON(@SettingsJson, '$.globalActions')
WHERE JSON_VALUE([value], '$.id') = 'cart_review_request';
IF @CartReviewGlobalActionIndex IS NULL
    THROW 51000, 'SeedCJDistribuciones: accion global de consulta de carrito no encontrada.', 1;
DECLARE @CartReviewGlobalActionPath NVARCHAR(200) = CONCAT('$.globalActions[', @CartReviewGlobalActionIndex, ']');

DECLARE @CartAppliedOutcome NVARCHAR(MAX) = N'{"response":{"guidance":"Confirma unicamente los cambios aplicados y pregunta si desea agregar algo mas."},"effects":[{"type":"facts.clear","facts":["order_finalized","cart_review_confirmed","order_checkout_presented","customer_confirmed"]},{"type":"presentation.add","template":"cart_changes_applied","mode":"Exclusive","priority":"Required"}]}';DECLARE @PartialCartOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Da un resultado explicito para cada referencia del lote usando la presentacion deterministica: agregada, sin existencia, ambigua, sugerida, cantidad insuficiente o no encontrada. No omitas referencias ni las mezcles entre categorias."},"effects":[{"type":"presentation.add","template":"cart_partial","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductSuggestionOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Presenta la sugerencia devuelta y pide confirmacion explicita antes de agregarla."},"effects":[{"type":"presentation.add","template":"product_ambiguity","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductUnavailableOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Indica que la referencia identificada no esta disponible y solicita otra opcion; no afirmes que fue agregada."},"effects":[{"type":"presentation.add","template":"cart_product_unavailable","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';
DECLARE @ProductNotFoundOutcome NVARCHAR(MAX) = N'{"response":{"mode":"ask_clarification","guidance":"Indica las referencias que no tuvieron coincidencia segura y solicita datos mas precisos; no afirmes que el carrito cambio."},"effects":[{"type":"presentation.add","template":"cart_not_found","dataPath":"error.context","mode":"Exclusive","priority":"Required"}]}';

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_partial',
    N'Procesé cada producto de tu solicitud:\r\n{{#if applied_items}}\r\n*Agregados*\r\n{{#each applied_items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if unavailable_items}}\r\n*Sin existencia*\r\n{{#each unavailable_items}}\r\n- {{product_text}}{{#if recognized_name}} ({{recognized_name}}){{/if}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if insufficient_stock_items}}\r\n*Existencia insuficiente*\r\n{{#each insufficient_stock_items}}\r\n- {{product_text}}: solicitaste {{requested_quantity}} y hay {{available_quantity}}; puedes pedir hasta {{maximum_command_quantity}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if ambiguous_options}}\r\n*Necesito que elijas*\r\n{{#each ambiguous_options}}\r\n- Para {{product_text}}: {{name}} — ${{unit_price}} {{currency}}\r\n{{/each}}\r\n{{/if}}\r\n{{#if suggested_options}}\r\n*Necesito confirmar*\r\n{{#each suggested_options}}\r\n- Para {{product_text}}: ¿te refieres a {{name}} — ${{unit_price}} {{currency}}?\r\n{{/each}}\r\n{{/if}}\r\n{{#if not_found_items}}\r\n*No encontrados*\r\n{{#each not_found_items}}\r\n- {{product_text}}\r\n{{/each}}\r\n{{/if}}\r\n*Pedido actual*\r\n{{#each items}}\r\n- {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} x{{quantity}}: ${{line_total}}\r\n{{/each}}\r\n\r\n*Total: ${{total}} {{currency}}*\r\n\r\n{{#if can_finalize_with_pending}}Si eso es todo, dejaré fuera las referencias sin existencia o sin coincidencia segura. ¿Eso sería todo o deseas agregar algo más?{{else}}Indícame las elecciones o una referencia más precisa para los pendientes.{{/if}}');

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
Listo, apliqué estos cambios al pedido:
{{#each display_applied_items}}
{{#if removed}}- Retiré {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} del carrito{{else}}- Agregué o actualicé {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}{{/if}}
{{/each}}

{{#if can_finalize_with_pending}}Si eso es todo, dejaré fuera las referencias sin existencia o sin coincidencia segura. ¿Eso sería todo o deseas agregar algo más?{{else}}¿Deseas agregar algo mas?{{/if}}
{{else}}
',
        JSON_VALUE(@SettingsJson, '$.templates.cart_partial'),
        N'
{{/if}}'));
SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_not_found',
    N'No agregue estas referencias porque no encontre una coincidencia segura:\r\n{{#each issues}}\r\n- {{ProductText}}\r\n{{/each}}\r\n\r\n{{#if can_finalize_with_pending}}Si eso es todo, las dejare fuera. ¿Eso sería todo o deseas agregar algo más?{{else}}Indicame el nombre, marca, presentacion o codigo de una de ellas para identificarla.{{/if}}');

SET @SettingsJson = JSON_MODIFY(@SettingsJson, '$.templates.cart_product_unavailable',
    N'Reconozco la referencia "{{product_text}}", pero actualmente no tiene disponibilidad comercial para agregarla.\r\n\r\n{{#if can_finalize_with_pending}}Si eso es todo, la dejare fuera. ¿Eso sería todo o deseas agregar algo más?{{else}}No hice cambios al pedido por esta referencia. Puedes indicarme otra marca, presentacion o producto.{{/if}}');

IF JSON_VALUE(@SettingsJson, '$.globalActions[1].actions[0].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedCJDistribuciones: ruta global de carrito inesperada.', 1;
IF JSON_VALUE(@SettingsJson, '$.flows[0].stages[2].actions[2].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedCJDistribuciones: ruta product_selection de carrito inesperada.', 1;
IF JSON_VALUE(@SettingsJson, '$.flows[0].stages[3].actions[1].operation') <> 'commerce.apply_order_changes'
    THROW 51000, 'SeedCJDistribuciones: ruta cart_review de carrito inesperada.', 1;

DECLARE @CartExecutionPaths TABLE (Path NVARCHAR(400) NOT NULL);
INSERT INTO @CartExecutionPaths (Path) VALUES
    (N'$.globalActions[1].actions[0].execution'),
    (N'$.flows[0].stages[2].actions[2].execution'),
    (N'$.flows[0].stages[3].actions[1].execution');

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
    (N'$.flows[0].stages[2].actions[2].onOutcome'),
    (N'$.flows[0].stages[3].actions[1].onOutcome');

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
