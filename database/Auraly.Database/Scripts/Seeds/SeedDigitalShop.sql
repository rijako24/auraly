SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

SET NOCOUNT ON;

DECLARE @TenantId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000001';
DECLARE @BusinessId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000010';
DECLARE @AgentId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000020';
DECLARE @OperationsAgentId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000021';
DECLARE @SubscriptionId UNIQUEIDENTIFIER = 'D1617A10-0000-0000-0000-000000000030';
DECLARE @AgentTypeId UNIQUEIDENTIFIER;
DECLARE @PlanId UNIQUEIDENTIFIER;

MERGE dbo.Tenants AS target
USING (SELECT @TenantId TenantId, N'Digital Shop' [Name], N'admin@digitalshop.co' Email) AS source
ON target.TenantId = source.TenantId
WHEN MATCHED THEN UPDATE SET [Name] = source.[Name], Email = source.Email, IsActive = 1, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (TenantId, [Name], Email, IsActive, CreatedAt)
VALUES (source.TenantId, source.[Name], source.Email, 1, SYSUTCDATETIME());

MERGE dbo.Businesses AS target
USING (SELECT @BusinessId BusinessId) AS source
ON target.BusinessId = source.BusinessId
WHEN MATCHED THEN UPDATE SET
    TenantId = @TenantId,
    [Name] = N'Digital Shop',
    [Description] = N'Venta presencial de celulares nuevos y usados, principalmente iPhone.',
    TimeZone = N'America/Bogota',
    IsActive = 1,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (BusinessId, TenantId, [Name], [Description], [Address], Phone, Email, Website, TimeZone, IsActive, CreatedAt)
VALUES
    (@BusinessId, @TenantId, N'Digital Shop',
     N'Venta presencial de celulares nuevos y usados, principalmente iPhone.',
     N'Pendiente de configurar', N'', N'admin@digitalshop.co', N'', N'America/Bogota', 1, SYSUTCDATETIME());

SELECT TOP (1) @AgentTypeId = AgentTypeId FROM dbo.AgentTypes WHERE IsActive = 1 ORDER BY [Name];
IF @AgentTypeId IS NULL
    THROW 51000, 'SeedDigitalShop: no existe un AgentType activo.', 1;

DECLARE @Catalog TABLE
(
    Sku NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(250) NOT NULL,
    Generation INT NOT NULL,
    StorageGb INT NOT NULL,
    UsedPrice DECIMAL(18,2) NOT NULL,
    NewPrice DECIMAL(18,2) NOT NULL,
    UsedSource NVARCHAR(1000) NOT NULL,
    NewSource NVARCHAR(1000) NOT NULL,
    ImageUrl NVARCHAR(1500) NOT NULL,
    TechnicalDescription NVARCHAR(1000) NULL
);

INSERT INTO @Catalog
    (Sku, [Name], Generation, StorageGb, UsedPrice, NewPrice, UsedSource, NewSource, ImageUrl)
VALUES
(N'IPH-11-64', N'iPhone 11', 11, 64, 599000, 949000, N'https://iphoneshopbogota.com/precios/iphone-11', N'https://iphoneshopbogota.com/precios/iphone-11', N'products/catalog/iphone-11.png'),
(N'IPH-11-PRO-64', N'iPhone 11 Pro', 11, 64, 899000, 1249000, N'https://iphoneshopbogota.com/precios/iphone-11', N'https://iphoneshopbogota.com/precios/iphone-11', N'products/catalog/iphone-11-pro.png'),
(N'IPH-11-PM-64', N'iPhone 11 Pro Max', 11, 64, 999000, 1399000, N'https://iphoneshopbogota.com/precios/iphone-11', N'https://iphoneshopbogota.com/precios/iphone-11', N'products/catalog/iphone-11-pro-max.png'),
(N'IPH-12-MINI-64', N'iPhone 12 mini', 12, 64, 849000, 1199000, N'https://iphoneshopbogota.com/precios/iphone-12', N'https://iphoneshopbogota.com/precios/iphone-12', N'products/catalog/iphone-12-mini.png'),
(N'IPH-12-64', N'iPhone 12', 12, 64, 899000, 1399000, N'https://iphoneshopbogota.com/precios/iphone-12', N'https://iphoneshopbogota.com/precios/iphone-12', N'products/catalog/iphone-12.png'),
(N'IPH-12-PRO-128', N'iPhone 12 Pro', 12, 128, 1149000, 1649000, N'https://iphoneshopbogota.com/precios/iphone-12', N'https://iphoneshopbogota.com/precios/iphone-12', N'products/catalog/iphone-12-pro.png'),
(N'IPH-12-PM-128', N'iPhone 12 Pro Max', 12, 128, 1449000, 1899000, N'https://iphoneshopbogota.com/precios/iphone-12', N'https://iphoneshopbogota.com/precios/iphone-12', N'products/catalog/iphone-12-pro-max.png'),
(N'IPH-13-MINI-128', N'iPhone 13 mini', 13, 128, 1149000, 1599000, N'https://iphoneshopbogota.com/precios/iphone-13', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-13-mini.png'),
(N'IPH-13-128', N'iPhone 13', 13, 128, 1199000, 2599000, N'https://iphoneshopbogota.com/precios/iphone-13', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-13.png'),
(N'IPH-13-PRO-128', N'iPhone 13 Pro', 13, 128, 1599000, 2199000, N'https://iphoneshopbogota.com/precios/iphone-13', N'https://iphoneshopbogota.com/precios/iphone-13', N'products/catalog/iphone-13-pro.png'),
(N'IPH-13-PM-128', N'iPhone 13 Pro Max', 13, 128, 1749000, 2499000, N'https://iphoneshopbogota.com/precios/iphone-13', N'https://iphoneshopbogota.com/precios/iphone-13', N'products/catalog/iphone-13-pro-max.png'),
(N'IPH-14-128', N'iPhone 14', 14, 128, 1449000, 1999000, N'https://iphoneshopbogota.com/precios/iphone-14', N'https://iphoneshopbogota.com/precios/iphone-14', N'products/catalog/iphone-14.png'),
(N'IPH-14-PLUS-128', N'iPhone 14 Plus', 14, 128, 1599000, 2299000, N'https://iphoneshopbogota.com/precios/iphone-14', N'https://iphoneshopbogota.com/precios/iphone-14', N'products/catalog/iphone-14-plus.png'),
(N'IPH-14-PRO-128', N'iPhone 14 Pro', 14, 128, 1899000, 2599000, N'https://iphoneshopbogota.com/precios/iphone-14', N'https://iphoneshopbogota.com/precios/iphone-14', N'products/catalog/iphone-14-pro.png'),
(N'IPH-14-PM-128', N'iPhone 14 Pro Max', 14, 128, 2149000, 2899000, N'https://iphoneshopbogota.com/precios/iphone-14', N'https://iphoneshopbogota.com/precios/iphone-14', N'products/catalog/iphone-14-pro-max.png'),
(N'IPH-15-128', N'iPhone 15', 15, 128, 1799000, 3539000, N'https://iphoneshopbogota.com/precios/iphone-15', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-15.png'),
(N'IPH-15-PLUS-128', N'iPhone 15 Plus', 15, 128, 2049000, 3899000, N'https://iphoneshopbogota.com/precios/iphone-15', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-15-plus.png'),
(N'IPH-15-PRO-128', N'iPhone 15 Pro', 15, 128, 2249000, 3499000, N'https://iphoneshopbogota.com/precios/iphone-15', N'https://iphoneshopbogota.com/precios/iphone-15', N'products/catalog/iphone-15-pro.png'),
(N'IPH-15-PM-256', N'iPhone 15 Pro Max', 15, 256, 2749000, 4099000, N'https://iphoneshopbogota.com/precios/iphone-15', N'https://iphoneshopbogota.com/precios/iphone-15', N'products/catalog/iphone-15-pro-max.png'),
(N'IPH-16E-128', N'iPhone 16e', 16, 128, 1999000, 2999000, N'https://iphoneshopbogota.com/precios/iphone-16', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-16e.png'),
(N'IPH-16-128', N'iPhone 16', 16, 128, 2349000, 2799000, N'https://iphoneshopbogota.com/precios/iphone-16', N'https://iphoneshopbogota.com/precios/iphone-16', N'products/catalog/iphone-16.png'),
(N'IPH-16-PLUS-128', N'iPhone 16 Plus', 16, 128, 2649000, 4799000, N'https://iphoneshopbogota.com/precios/iphone-16', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-16-plus.png'),
(N'IPH-16-PRO-128', N'iPhone 16 Pro', 16, 128, 2899000, 6469000, N'https://iphoneshopbogota.com/precios/iphone-16', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-16-pro.png'),
(N'IPH-16-PM-256', N'iPhone 16 Pro Max', 16, 256, 3399000, 6999000, N'https://iphoneshopbogota.com/precios/iphone-16', N'https://mac-center.com/pages/iphone?page=1', N'products/catalog/iphone-16-pro-max.png'),
(N'IPH-17E-256', N'iPhone 17e', 17, 256, 2799000, 3499000, N'https://iphoneshopbogota.com/precios/iphone-17', N'https://mac-center.com/products/iphone-17e-mhrw4lz-a', N'products/catalog/iphone-17e.png'),
(N'IPH-17-256', N'iPhone 17', 17, 256, 3199000, 3349000, N'https://iphoneshopbogota.com/precios/iphone-17', N'https://iphoneshopbogota.com/precios/iphone-17', N'products/catalog/iphone-17.png'),
(N'IPH-AIR-256', N'iPhone Air', 17, 256, 4299000, 5299000, N'https://iphoneshopbogota.com/precios/iphone-17', N'https://mac-center.com/products/iphone-air-mg2n4lz-a', N'products/catalog/iphone-air.png'),
(N'IPH-17-PRO-256', N'iPhone 17 Pro', 17, 256, 4149000, 4349000, N'https://iphoneshopbogota.com/precios/iphone-17', N'https://iphoneshopbogota.com/precios/iphone-17', N'products/catalog/iphone-17-pro.png'),
(N'IPH-17-PM-256', N'iPhone 17 Pro Max', 17, 256, 4399000, 4599000, N'https://iphoneshopbogota.com/precios/iphone-17', N'https://iphoneshopbogota.com/precios/iphone-17', N'products/catalog/iphone-17-pro-max.png');

UPDATE @Catalog
SET TechnicalDescription = CASE Sku
    WHEN N'IPH-11-64' THEN N'• Pantalla: Liquid Retina HD LCD de 6.1 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara dual de 12 MP con gran angular y ultra gran angular' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 17 horas de reproduccion de video.'
    WHEN N'IPH-11-PRO-64' THEN N'• Pantalla: Super Retina XDR OLED de 5.8 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro de tres camaras de 12 MP con gran angular, ultra gran angular y teleobjetivo' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 18 horas de reproduccion de video.'
    WHEN N'IPH-11-PM-64' THEN N'• Pantalla: Super Retina XDR OLED de 6.5 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro de tres camaras de 12 MP con gran angular, ultra gran angular y teleobjetivo' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 20 horas de reproduccion de video.'
    WHEN N'IPH-12-MINI-64' THEN N'• Pantalla: Super Retina XDR OLED de 5.4 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara dual de 12 MP' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 15 horas de reproduccion de video.'
    WHEN N'IPH-12-64' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara dual de 12 MP' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 17 horas de reproduccion de video.'
    WHEN N'IPH-12-PRO-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro de tres camaras de 12 MP y escaner LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 17 horas de reproduccion de video.'
    WHEN N'IPH-12-PM-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.7 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro de tres camaras de 12 MP, teleobjetivo 2.5x y escaner LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 20 horas de reproduccion de video.'
    WHEN N'IPH-13-MINI-128' THEN N'• Pantalla: Super Retina XDR OLED de 5.4 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara dual de 12 MP con estabilizacion por desplazamiento del sensor' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 17 horas de reproduccion de video.'
    WHEN N'IPH-13-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara dual de 12 MP con estabilizacion por desplazamiento del sensor' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 19 horas de reproduccion de video.'
    WHEN N'IPH-13-PRO-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas con ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: tres camaras Pro de 12 MP y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 22 horas de reproduccion de video.'
    WHEN N'IPH-13-PM-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.7 pulgadas con ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: tres camaras Pro de 12 MP y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 28 horas de reproduccion de video.'
    WHEN N'IPH-14-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara dual de 12 MP' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 20 horas de video.'
    WHEN N'IPH-14-PLUS-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.7 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara dual de 12 MP' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 26 horas de video.'
    WHEN N'IPH-14-PRO-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas con Dynamic Island, pantalla siempre activa y ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara principal de 48 MP, teleobjetivo y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 23 horas de video.'
    WHEN N'IPH-14-PM-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.7 pulgadas con Dynamic Island, pantalla siempre activa y ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara principal de 48 MP, teleobjetivo y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 29 horas de video.'
    WHEN N'IPH-15-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas con Dynamic Island' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema dual con camara principal de 48 MP y teleobjetivo 2x de calidad optica' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 20 horas de video.'
    WHEN N'IPH-15-PLUS-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.7 pulgadas con Dynamic Island' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema dual con camara principal de 48 MP y teleobjetivo 2x de calidad optica' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 26 horas de video.'
    WHEN N'IPH-15-PRO-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas con ProMotion de hasta 120 Hz, pantalla siempre activa y Dynamic Island' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro de 48 MP, teleobjetivo 3x y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 23 horas de video.'
    WHEN N'IPH-15-PM-256' THEN N'• Pantalla: Super Retina XDR OLED de 6.7 pulgadas con ProMotion de hasta 120 Hz, pantalla siempre activa y Dynamic Island' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro de 48 MP, teleobjetivo 5x y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 29 horas de video.'
    WHEN N'IPH-16E-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara Fusion de 48 MP con teleobjetivo 2x de calidad optica' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 26 horas de video.'
    WHEN N'IPH-16-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas con Dynamic Island' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara Fusion de 48 MP, teleobjetivo 2x y ultra gran angular' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 22 horas de video.'
    WHEN N'IPH-16-PLUS-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.7 pulgadas con Dynamic Island' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara Fusion de 48 MP, teleobjetivo 2x y ultra gran angular' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 27 horas de video.'
    WHEN N'IPH-16-PRO-128' THEN N'• Pantalla: Super Retina XDR OLED de 6.3 pulgadas con ProMotion de hasta 120 Hz y pantalla siempre activa' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro con Fusion de 48 MP, ultra gran angular de 48 MP, teleobjetivo 5x y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 27 horas de video.'
    WHEN N'IPH-16-PM-256' THEN N'• Pantalla: Super Retina XDR OLED de 6.9 pulgadas con ProMotion de hasta 120 Hz y pantalla siempre activa' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro con Fusion de 48 MP, ultra gran angular de 48 MP, teleobjetivo 5x y LiDAR' + NCHAR(13) + NCHAR(10) + N'• Autonomía: hasta 33 horas de video.'
    WHEN N'IPH-17E-256' THEN N'• Pantalla: Super Retina XDR OLED de 6.1 pulgadas' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara Fusion de 48 MP con teleobjetivo 2x de calidad optica'
    WHEN N'IPH-17-256' THEN N'• Pantalla: Super Retina XDR OLED de 6.3 pulgadas con pantalla siempre activa y ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema dual Fusion de 48 MP'
    WHEN N'IPH-AIR-256' THEN N'• Pantalla: Super Retina XDR OLED de 6.5 pulgadas con pantalla siempre activa y ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: camara Fusion de 48 MP'
    WHEN N'IPH-17-PRO-256' THEN N'• Pantalla: Super Retina XDR OLED de 6.3 pulgadas con pantalla siempre activa y ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro Fusion de 48 MP, LiDAR y camara frontal Center Stage de 18 MP'
    WHEN N'IPH-17-PM-256' THEN N'• Pantalla: Super Retina XDR OLED de 6.9 pulgadas con pantalla siempre activa y ProMotion de hasta 120 Hz' + NCHAR(13) + NCHAR(10) + N'• Cámara: sistema Pro Fusion de 48 MP, LiDAR y camara frontal Center Stage de 18 MP'
END;

MERGE dbo.Products AS target
USING @Catalog AS source
ON target.BusinessId = @BusinessId AND target.Sku = source.Sku
WHEN MATCHED THEN UPDATE SET
    [Name] = source.[Name],
    [Description] = source.TechnicalDescription,
    CategoryName = N'iPhone ' + CONVERT(NVARCHAR(10), source.Generation),
    Currency = N'COP',
    ManageStock = 0,
    IsActive = 1,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ProductId, BusinessId, Source, Sku, [Name], [Description], CategoryName,
     Currency, ManageStock, IsActive, CreatedAt)
VALUES
    (NEWID(), @BusinessId, 0, source.Sku, source.[Name],
     source.TechnicalDescription,
     N'iPhone ' + CONVERT(NVARCHAR(10), source.Generation),
     N'COP', 0, 1, SYSUTCDATETIME());

MERGE dbo.ProductPrices AS target
USING (
    SELECT product.ProductId, catalog.UsedPrice AS Amount
    FROM @Catalog catalog
    JOIN dbo.Products product ON product.BusinessId=@BusinessId AND product.Sku=catalog.Sku
) AS source
ON target.BusinessId=@BusinessId AND target.ProductId=source.ProductId AND target.IsActive=1
WHEN NOT MATCHED THEN INSERT
    (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,InputMode,ValidFrom,IsActive,CreatedAt)
VALUES
    (NEWID(),@BusinessId,source.ProductId,source.Amount,source.Amount,N'COP',N'SalePrice',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());

DECLARE @Offers TABLE
(
    ProductId UNIQUEIDENTIFIER NOT NULL,
    Condition NVARCHAR(30) NOT NULL,
    StorageGb INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    MinimumBatteryHealthPercent INT NULL,
    SourceUrl NVARCHAR(1000) NOT NULL
);

INSERT INTO @Offers (ProductId, Condition, StorageGb, UnitPrice, MinimumBatteryHealthPercent, SourceUrl)
SELECT p.ProductId, N'used', c.StorageGb, c.UsedPrice, 91, c.UsedSource
FROM @Catalog c
JOIN dbo.Products p ON p.BusinessId = @BusinessId AND p.Sku = c.Sku
UNION ALL
SELECT p.ProductId, N'new', c.StorageGb, c.NewPrice, NULL, c.NewSource
FROM @Catalog c
JOIN dbo.Products p ON p.BusinessId = @BusinessId AND p.Sku = c.Sku;

MERGE dbo.ProductOffers AS target
USING @Offers AS source
ON target.ProductId = source.ProductId
AND target.Condition = source.Condition
AND target.StorageGb = source.StorageGb
AND target.Color IS NULL
WHEN MATCHED THEN UPDATE SET
    UnitPrice = source.UnitPrice,
    Currency = N'COP',
    MinimumBatteryHealthPercent = source.MinimumBatteryHealthPercent,
    IsAvailable = 1,
    IsActive = 1,
    PriceSourceUrl = source.SourceUrl,
    PriceObservedAtUtc = CONVERT(DATETIME2, '2026-07-27T00:00:00'),
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ProductOfferId, ProductId, BusinessId, Condition, StorageGb, Color, UnitPrice, Currency,
     MinimumBatteryHealthPercent, IsAvailable, IsActive, PriceSourceUrl, PriceObservedAtUtc, CreatedAt)
VALUES
    (NEWID(), source.ProductId, @BusinessId, source.Condition, source.StorageGb, NULL,
     source.UnitPrice, N'COP', source.MinimumBatteryHealthPercent, 1, 1, source.SourceUrl,
     CONVERT(DATETIME2, '2026-07-27T00:00:00'), SYSUTCDATETIME());

MERGE dbo.ProductImages AS target
USING (
    SELECT p.ProductId, c.ImageUrl, c.[Name]
    FROM @Catalog c
    JOIN dbo.Products p ON p.BusinessId = @BusinessId AND p.Sku = c.Sku
) AS source
ON target.ProductId = source.ProductId
AND target.ProductOfferId IS NULL
AND target.IsPrimary = 1
WHEN MATCHED THEN UPDATE SET
    MediaUrl = source.ImageUrl,
    AltText = source.[Name],
    DisplayOrder = 0,
    IsPrimary = 1,
    IsActive = 1,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ProductImageId, ProductId, BusinessId, ProductOfferId, MediaUrl, AltText,
     DisplayOrder, IsPrimary, IsActive, CreatedAt)
VALUES
    (NEWID(), source.ProductId, @BusinessId, NULL, source.ImageUrl, source.[Name],
     0, 1, 1, SYSUTCDATETIME());

DECLARE @AccessoryCatalog TABLE
(
    Sku NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(250) NOT NULL,
    [Description] NVARCHAR(1000) NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL
);

INSERT INTO @AccessoryCatalog (Sku, [Name], [Description], UnitPrice)
VALUES
(N'ACC-CUBO-20W', N'Cubo original 20W', N'Cargador, cubo o adaptador de corriente original USB-C de 20W.', 70000),
(N'ACC-CABLE-TC-LIGHTNING', N'Cable USB-C a Lightning original', N'Cable original USB-C a Lightning para iPhone.', 45000),
(N'ACC-CABLE-TC-TC', N'Cable USB-C a USB-C original', N'Cable original USB-C a USB-C para carga.', 65000);

MERGE dbo.Products AS target
USING @AccessoryCatalog AS source
ON target.BusinessId = @BusinessId AND target.Sku = source.Sku
WHEN MATCHED THEN UPDATE SET
    [Name] = source.[Name], [Description] = source.[Description], CategoryName = N'Accesorios',
    Currency = N'COP', ManageStock = 0, IsActive = 1,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ProductId, BusinessId, Source, Sku, [Name], [Description], CategoryName,
     Currency, ManageStock, IsActive, CreatedAt)
VALUES
    (NEWID(), @BusinessId, 0, source.Sku, source.[Name], source.[Description],
     N'Accesorios', N'COP', 0, 1, SYSUTCDATETIME());

MERGE dbo.ProductPrices AS target
USING (
    SELECT product.ProductId, accessory.UnitPrice AS Amount
    FROM @AccessoryCatalog accessory
    JOIN dbo.Products product ON product.BusinessId=@BusinessId AND product.Sku=accessory.Sku
) AS source
ON target.BusinessId=@BusinessId AND target.ProductId=source.ProductId AND target.IsActive=1
WHEN NOT MATCHED THEN INSERT
    (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,InputMode,ValidFrom,IsActive,CreatedAt)
VALUES
    (NEWID(),@BusinessId,source.ProductId,source.Amount,source.Amount,N'COP',N'SalePrice',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());

MERGE dbo.ProductOffers AS target
USING (
    SELECT p.ProductId, a.UnitPrice
    FROM @AccessoryCatalog a
    JOIN dbo.Products p ON p.BusinessId = @BusinessId AND p.Sku = a.Sku
) AS source
ON target.ProductId = source.ProductId AND target.Condition = N'new'
   AND target.StorageGb IS NULL AND target.Color IS NULL AND target.VariantLabel IS NULL
WHEN MATCHED THEN UPDATE SET UnitPrice = source.UnitPrice, Currency = N'COP',
    IsAvailable = 1, IsActive = 1, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ProductOfferId, ProductId, BusinessId, Condition, StorageGb, Color, VariantLabel,
     UnitPrice, Currency, IsAvailable, IsActive, CreatedAt)
VALUES
    (NEWID(), source.ProductId, @BusinessId, N'new', NULL, NULL, NULL,
     source.UnitPrice, N'COP', 1, 1, SYSUTCDATETIME());
DECLARE @ChargerProductId UNIQUEIDENTIFIER =
(
    SELECT ProductId FROM dbo.Products
    WHERE BusinessId = @BusinessId AND Sku = N'ACC-CUBO-20W' AND IsActive = 1
);

IF @ChargerProductId IS NULL
    THROW 51000, 'SeedDigitalShop: no existe el cargador recomendado ACC-CUBO-20W.', 1;

DECLARE @PhoneRecommendationRules TABLE
(
    ProductRecommendationRuleId UNIQUEIDENTIFIER NOT NULL,
    CategoryName NVARCHAR(300) NOT NULL
);

INSERT INTO @PhoneRecommendationRules (ProductRecommendationRuleId, CategoryName)
VALUES
('D1617A10-0000-0000-0000-000000000200', N'iPhone 11'),
('D1617A10-0000-0000-0000-000000000201', N'iPhone 12'),
('D1617A10-0000-0000-0000-000000000202', N'iPhone 13'),
('D1617A10-0000-0000-0000-000000000203', N'iPhone 14'),
('D1617A10-0000-0000-0000-000000000204', N'iPhone 15'),
('D1617A10-0000-0000-0000-000000000205', N'iPhone 16'),
('D1617A10-0000-0000-0000-000000000206', N'iPhone 17');

MERGE dbo.ProductRecommendationRules AS target
USING @PhoneRecommendationRules AS source
ON target.BusinessId = @BusinessId
AND target.ProductRecommendationRuleId = source.ProductRecommendationRuleId
WHEN MATCHED THEN UPDATE SET
    IntegrationConnectionId = NULL, MatchType = 1, SourceProductId = NULL,
    SourceValue = source.CategoryName, RecommendedProductId = @ChargerProductId,
    RecommendedExternalProductId = NULL, RecommendedSku = N'ACC-CUBO-20W',
    RecommendedSearchText = N'cubo original 20W', RecommendationType = 0,
    Priority = 100,
    Reason = N'Para acompañar este iPhone con una carga adecuada, te recomiendo el cubo original de 20W.',
    IsActive = 1, StartsAtUtc = NULL, EndsAtUtc = NULL, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ProductRecommendationRuleId, BusinessId, IntegrationConnectionId, MatchType,
     SourceProductId, SourceValue, RecommendedProductId, RecommendedExternalProductId,
     RecommendedSku, RecommendedSearchText, RecommendationType, Priority, Reason,
     IsActive, StartsAtUtc, EndsAtUtc, CreatedAt)
VALUES
    (source.ProductRecommendationRuleId, @BusinessId, NULL, 1,
     NULL, source.CategoryName, @ChargerProductId, NULL,
     N'ACC-CUBO-20W', N'cubo original 20W', 0, 100,
     N'Para acompañar este iPhone con una carga adecuada, te recomiendo el cubo original de 20W.',
     1, NULL, NULL, SYSUTCDATETIME());
DECLARE @SettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.2,
  "historyWindowSize": 20,
  "extractorHistoryWindowSize": 8,
  "persona": "Eres Catalina, asesora comercial de Digital Shop. Hablas en espanol colombiano como una vendedora real: cercana, espontanea, segura y facil de entender. Conversas con palabras cotidianas, reaccionas a lo que la persona acaba de decir y das tu opinion comercial en primera persona cuando sea util. Puedes usar giros como mira, en este caso yo te recomendaria o esta opcion me gusta por, solo como ejemplos de tono: varia siempre la redaccion y nunca los conviertas en frases fijas. Explica por que recomiendas algo usando datos autoritativos del producto, precio, condicion, garantia o bateria. Evita sonar como ficha automatica, formulario o robot; tampoco repitas perfecto, excelente eleccion, si quieres ni la misma invitacion en cada turno. Escribes para WhatsApp con parrafos cortos, listas legibles y un emoji pertinente de vez en cuando. No uses punto y coma para separar datos: usa un salto de linea o una frase corta. El catalogo y los resultados de las operaciones son la fuente de verdad comercial.",
  "policies": "## ALCANCE\n\n- Atiende consultas de compra de iPhone, accesorios y servicio tecnico.\n- Los unicos telefonos vendidos por Digital Shop son iPhone.\n- El bot informa, asesora y orienta; la compra se termina presencialmente.\n\n## FUENTE DE VERDAD\n\n- Presenta precios, capacidades, colores, disponibilidad e imagenes exclusivamente desde resultados autoritativos del catalogo del turno.\n- Presenta especificaciones tecnicas exclusivamente desde la descripcion autoritativa del producto.\n- Si un dato no aparece en una fuente autoritativa, dilo brevemente y no lo inventes.\n- No reconstruyas ni repitas un bloque autoritativo ya presentado en la misma conversacion.\n\n## HECHOS COMERCIALES\n\n- En equipos nuevos, la garantia es directamente con la marca; no inventes duracion ni cobertura.\n- En equipos usados, la bateria es superior al 90% y el porcentaje exacto se muestra en tienda; no inventes un valor concreto ni una garantia no informada.\n- Conserva la continuidad del historial y responde a la intencion actual sin volver a ofrecer informacion que ya acabas de presentar.",
  "conversationOpening": { "enabled": true, "allowQuestions": false, "guidance": "El unico contenido de la apertura es exactamente: Hola, bienvenido a Digital Shop 👋 Soy Catalina, un gusto saludarte. La apertura termina al final de esa frase. No agregues un segundo parrafo, pregunta, respuesta a la solicitud actual ni informacion de productos dentro de la apertura. Cuando el renderer general incluya una continuacion, esa continuacion pertenece exclusivamente a la etapa y va separada por una linea en blanco." },
  "failureResponses": { "llmUnavailable": "En este momento no pude consultar el catalogo. Intenta nuevamente en unos minutos." },
  "conversationFollowUp": { "enabled": true, "delayMinutes": 240, "respectOperatingHours": false, "guidance": "Retoma con empatia y sin presionar. Empieza con: Parece que estas ocupado, pero no te preocupes 😊. Aqui estoy para ayudarte a encontrar el iPhone ideal cuando tengas un momento. Si falta un dato, agrega una sola pregunta suave sobre el modelo o si lo quiere nuevo o usado." },
  "factSchema": [
    { "key": "device_model", "role": "commerce.product_query", "label": "modelo de iPhone", "type": "string", "source": "user", "scope": "request", "extractionGuidance": "Modelo completo solicitado, por ejemplo iPhone 15 Pro Max. Conserva el modelo ya conocido cuando el cliente solo diga una capacidad como 128 o 256. Normaliza la expresion hablada ''Pro mas'' o ''Pro más'' al modelo ''Pro Max'', y nunca la reduzcas a ''Pro''. No agregues condicion ni capacidad." },
    { "key": "product_condition", "role": "commerce.product_condition", "label": "condicion", "type": "string", "source": "user", "scope": "request", "dependsOn": ["device_model"], "options": [{ "label": "Nuevo", "selector": "A", "value": "new" }, { "label": "Usado", "selector": "B", "value": "used" }], "extractionGuidance": "La condicion pertenece al modelo vigente y nunca se hereda a un iPhone distinto. Si el mensaje actual dice nuevo, nueva, sellado o de caja, registra new. Si dice usado, usada, de segunda o seminuevo, registra used. Captura la condicion en el mismo turno aunque venga junto al modelo: por ejemplo, en ''y el iPhone 14 Pro Max usado'' establece a la vez device_model y product_condition=used para consultar inmediatamente. Cuando el cliente cambia de modelo y no dice nuevo ni usado en ese mismo mensaje, no infieras ni conserves la condicion anterior: deja que product_condition se invalide para volver a preguntarla. Los selectores A y B solo aplican cuando las opciones se presentaron en el mensaje inmediatamente anterior. No marques como ambigua una palabra de condicion explicita. Si menciona la otra condicion despues de haber hablado del mismo modelo, activa la senal de cambio de condicion para mostrarla y compararla brevemente." },
    { "key": "storage_gb", "label": "capacidad", "type": "integer", "source": "user", "scope": "request", "extractionGuidance": "Capacidad solicitada en GB, por ejemplo 128, 256 o 512. Un numero de capacidad nunca reemplaza device_model." },
    { "key": "offer_presented", "role": "commerce.offer_presented", "label": "oferta presentada", "type": "boolean", "source": "system", "scope": "ephemeral" },
    { "key": "automatic_model_comparison_used", "label": "comparacion automatica usada", "type": "boolean", "source": "system", "scope": "ephemeral" }
  ],
  "templates": {
    "new_product_offers": "📱 Acá te dejo toda la información del equipo:\r\n{{#each offers}}*{{product_name}} nuevo* · {{storage_gb}} GB{{#if color}} · {{color}}{{/if}}\r\n💰 ${{unit_price}} {{currency}}\r\n\r\n*Y estas son sus características principales*\r\n{{description}}\r\n{{/each}}\r\n✅ Al ser nuevo, la garantía es directamente con la marca.\r\n\r\nQuedo atenta 😊 Cuando quieras verlo en persona, puedes pasar por el local.",
    "used_product_offers": "📱 Acá te dejo toda la información del equipo:\r\n{{#each offers}}*{{product_name}} usado* · {{storage_gb}} GB{{#if color}} · {{color}}{{/if}}\r\n💰 ${{unit_price}} {{currency}}\r\n\r\n*Y estas son sus características principales*\r\n{{description}}\r\n{{/each}}\r\n🔋 La batería está por encima del 90%. El porcentaje exacto se revisa en tienda.\r\n\r\nQuedo atenta 😊 Cuando quieras verlo en persona, puedes pasar por el local.",
    "accessory_product_offers": "🔌 *Accesorios disponibles*\\r\\n{{#each offers}}- *{{product_name}}*{{#if variant}} · {{variant}}{{/if}}\\r\\n  ${{unit_price}} {{currency}}\\r\\n{{/each}}",
    "product_color_options": "🎨 *Colores disponibles para {{product_query}}*\r\n{{#if available_colors}}{{#each available_colors}}- {{this}}\r\n{{/each}}\r\nElige el que mejor vaya contigo.{{else}}El catálogo no tiene colores confirmados para esta referencia.{{/if}}",
    "iphone_model_catalog": "📱 *iPhone disponibles*\\r\\n{{#each products}}- {{name}}\\r\\n{{/each}}\\r\\nIndícame el modelo que te interesa y te presento sus opciones vigentes.",
    "compared_current_new_offer": "📱 *Modelo consultado*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}\r\n{{description}}\r\n{{/each}}\r\n✅ Garantía directamente con la marca.",
    "compared_previous_new_offer": "📱 *Modelo anterior*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}\r\n{{description}}\r\n{{/each}}\r\n✅ Garantía directamente con la marca.",
    "compared_current_used_offer": "📱 *Modelo consultado*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}\r\n{{description}}\r\n{{/each}}\r\n🔋 Batería superior al 90%; porcentaje exacto en tienda.",
    "compared_previous_used_offer": "📱 *Modelo anterior*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}\r\n{{description}}\r\n{{/each}}\r\n🔋 Batería superior al 90%; porcentaje exacto en tienda.",
    "phone_accessory_recommendation": "{{#each recommendations}}🔌 *Carga recomendada*\r\n- *{{name}}*: ${{unit_price}} {{currency}}\r\n{{reason}}{{/each}}",
    "technical_comparison_model": "{{#each products}}📱 *{{name}}*\r\n{{description}}\r\n{{/each}}",
    "technical_comparison_new_price": "{{#each offers}}💰 *{{product_name}} nuevo* · {{storage_gb}} GB{{#if color}} · {{color}}{{/if}}\r\n${{unit_price}} {{currency}} · garantia directamente con la marca.\r\n{{/each}}",
    "technical_comparison_used_price": "{{#each offers}}💰 *{{product_name}} usado* · {{storage_gb}} GB{{#if color}} · {{color}}{{/if}}\r\n${{unit_price}} {{currency}} · bateria superior al 90%; porcentaje exacto en tienda.\r\n{{/each}}",
    "switched_new_offer": "📱 *Ahora, nuevo*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB{{#if color}} · {{color}}{{/if}}\r\n💰 ${{unit_price}} {{currency}}\r\n{{/each}}\r\n✅ Garantía directamente con la marca.",
    "switched_used_offer": "📱 *Ahora, usado*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB{{#if color}} · {{color}}{{/if}}\r\n💰 ${{unit_price}} {{currency}}\r\n{{/each}}\r\n🔋 Batería superior al 90%; porcentaje exacto en tienda.",
    "condition_comparison_new": "📱 *Nuevo*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}{{/each}}\r\n✅ Garantía directamente con la marca.",
    "condition_comparison_used": "📱 *Usado*\r\n{{#each offers}}*{{product_name}}* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}{{/each}}\r\n🔋 Batería superior al 90%; porcentaje exacto en tienda.",
    "additional_new_offer": "📱 Acá te dejo toda la información del equipo:\r\n{{#each offers}}*{{product_name}} nuevo* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}\r\n\r\n*Y estas son sus características principales*\r\n{{description}}{{/each}}\r\n✅ Al ser nuevo, la garantía es directamente con la marca.\r\n\r\nQuedo atenta 😊 Cuando quieras verlo en persona, puedes pasar por el local.",
    "additional_used_offer": "📱 Acá te dejo toda la información del equipo:\r\n{{#each offers}}*{{product_name}} usado* · {{storage_gb}} GB\r\n💰 ${{unit_price}} {{currency}}\r\n\r\n*Y estas son sus características principales*\r\n{{description}}{{/each}}\r\n🔋 La batería está por encima del 90%. El porcentaje exacto se revisa en tienda.\r\n\r\nQuedo atenta 😊 Cuando quieras verlo en persona, puedes pasar por el local.",
    "store_location": "📍 *Digital Shop*\r\nCra. 12 #16B-06, barrio Loperena, Valledupar.\r\n\r\n🕒 *Horario de atención*\r\n- Lunes a viernes: 8:00 a. m.–12:00 m. y 2:00–6:00 p. m.\r\n- Sábados: 8:00 a. m.–12:00 m.",
    "technical_service_local": "🛠️ Claro, para servicio técnico puedes acercarte y con gusto revisamos tu equipo.\r\n\r\n📍 Cra. 12 #16B-06, barrio Loperena, Valledupar.\r\n🕒 Lunes a viernes: 8:00 a. m.–12:00 m. y 2:00–6:00 p. m. · Sábados: 8:00 a. m.–12:00 m."
  },
  "flows": [{
    "id": "iphone_sales",
    "type": "primary",
    "routingGuidance": "Asesoria comercial abierta: responde cualquier duda util, recomienda y compara; cotiza cuando conoce modelo y condicion, y orienta la compra o servicio tecnico al local.",
    "stages": [
      {
        "id": "discover",
        "name": "Modelo y condicion",
        "goal": "Identificar el modelo exacto y si lo quiere nuevo o usado.",
        "collect": ["device_model", "product_condition"],
        "advanceWhenFacts": ["device_model", "product_condition"],
        "awaitCustomerReply": true,
        "conversationGuidance": "Resuelve solo uno de estos casos por turno. Caso 1: si el mensaje es solamente un saludo, pregunta unicamente ¿En que puedo ayudarte el dia de hoy? y termina; no menciones iPhone ni las opciones de condicion. Caso 2: si el cliente quiere comprar un iPhone pero no indica modelo, pregunta solo que modelo busca. Caso 3: si indica el modelo sin condicion, reconocelo brevemente y formula una sola pregunta natural sobre como lo prefiere antes de presentar las opciones. Presenta las opciones canonicas de product_condition en bloques separados y deja una linea en blanco completa entre ambas. Conserva visibles los selectores: A. Nuevo, en una linea propia. Debajo indica Garantia directamente con la marca. Luego B. Usado, en una linea propia. Debajo indica Bateria superior al 90%. El porcentaje exacto se revisa en tienda. Termina inmediatamente despues de la explicacion de usado, sin decir que puede responder con la letra, sin ofrecer otra cosa y sin repetir la pregunta. No elogies el modelo como excelente eleccion, gran opcion ni con frases similares antes de consultar el catalogo. No uses las frases que estrena ni que permite ahorrar. Caso 4: si el mensaje actual ya incluye modelo y condicion, o responde new, used, nuevo o usado para el modelo vigente, establece response.mode en continue y no redactes texto: la etapa de cotizacion presenta la respuesta completa. No describas precio, disponibilidad, caracteristicas, color, bateria concreta ni garantia especifica antes de un resultado del catalogo.",
        "reentryOnFactChanged": ["device_model"]
      },
      {
        "id": "quote",
        "name": "Cotizacion vigente",
        "goal": "Consultar y presentar la oferta autoritativa con imagen.",
        "collect": ["device_model", "product_condition"],
        "advanceWhenFacts": ["offer_presented"],
        "reentryOnFactChanged": ["device_model", "product_condition"],
        "actions": [
          {
            "id": "search_new_offer",
            "operation": "commerce.search_product_offers",
            "trigger": "when_ready",
            "condition": { "all": [
              { "factPresent": "device_model" },
              { "factEquals": { "key": "product_condition", "value": "new" } },
              { "factMissing": "offer_presented" }
            ] },
            "arguments": { "product_query": "{{fact.device_model}}", "condition": "new" },
            "execution": { "idempotency": "input_version", "maxAttempts": 1 },
            "onOutcome": {
              "offers.found": {
                "effects": [
                  { "type": "fact.set", "fact": "offer_presented", "value": true },
                  { "type": "presentation.add", "template": "new_product_offers", "mode": "Exclusive", "priority": "Required" }
                ]
              },
              "offers.not_found": { "response": { "guidance": "Indica brevemente que no hay oferta nueva vigente para ese modelo. No preguntes si desea ver usado; recomienda revisar la alternativa usada disponible en el local sin afirmar precio ni existencia." } }
            }
          },
          {
            "id": "search_used_offer",
            "operation": "commerce.search_product_offers",
            "trigger": "when_ready",
            "condition": { "all": [
              { "factPresent": "device_model" },
              { "factEquals": { "key": "product_condition", "value": "used" } },
              { "factMissing": "offer_presented" }
            ] },
            "arguments": { "product_query": "{{fact.device_model}}", "condition": "used" },
            "execution": { "idempotency": "input_version", "maxAttempts": 1 },
            "onOutcome": {
              "offers.found": {
                "effects": [
                  { "type": "fact.set", "fact": "offer_presented", "value": true },
                  { "type": "presentation.add", "template": "used_product_offers", "mode": "Exclusive", "priority": "Required" }
                ]
              },
              "offers.not_found": { "response": { "guidance": "Indica brevemente que no hay oferta usada vigente para ese modelo. No hagas una cadena de preguntas ni inventes alternativas." } }
            }
          },
          {
            "id": "recommend_charger_after_phone",
            "operation": "commerce.search_products",
            "trigger": "when_ready",
            "condition": { "all": [
              { "factPresent": "device_model" },
              { "factPresent": "product_condition" },
              { "factMissing": "offer_presented" }
            ] },
            "arguments": { "query": "{{fact.device_model}}", "mode": "search_target", "limit": 5, "include_stock": false },
            "execution": { "idempotency": "once_per_request", "maxAttempts": 1 },
            "onOutcome": {
              "products.found": {
                "effects": [{ "type": "presentation.add", "template": "phone_accessory_recommendation", "mode": "Exclusive", "priority": "Required" }],
                "response": { "guidance": "Conserva los bloques autoritativos sin repetir sus datos. Si el catalogo devolvio una recomendacion de carga, presentala como complemento opcional. Puedes agregar una observacion comercial solo si responde al contexto concreto del cliente y esta sustentada por los datos visibles. No uses elogios genericos, no invites al local sin interes de compra y no cierres con preguntas de ofrecimiento." }
              },
              "products.not_found": { "response": { "guidance": "Presenta la oferta del iPhone sin inventar accesorios ni agregar elogios genericos o cierres de ofrecimiento." } },
              "catalog.not_ready": { "response": { "guidance": "Presenta la oferta del iPhone sin mencionar accesorios no verificados, elogios genericos ni cierres de ofrecimiento." } },
              "products.search_failed": { "response": { "guidance": "Presenta la oferta del iPhone sin mencionar accesorios no verificados, elogios genericos ni cierres de ofrecimiento." } }
            }
          }
        ],
        "awaitCustomerReply": true
      },
      {
        "id": "visit",
        "name": "Visita al local",
        "goal": "Cerrar la consulta cuando el cliente confirme que desea comprar o pagar.",
        "collect": ["device_model", "product_condition"],
        "signals": [{
          "type": "store_visit_confirmed",
          "description": "El cliente confirma que quiere comprar, pagar, separar o ir al local por el equipo.",
          "valueSchema": { "type": "object", "properties": { "confirmed": { "type": "boolean", "const": true } }, "required": ["confirmed"], "additionalProperties": false }
        }],
        "actions": [{
          "id": "complete_store_sale_lead",
          "operation": "conversation.complete_request",
          "trigger": "on_signal",
          "signal": "store_visit_confirmed",
          "arguments": { "confirmed": "{{signal.store_visit_confirmed.value.confirmed}}" },
          "execution": { "idempotency": "once_per_request", "maxAttempts": 1 },
          "onOutcome": {
            "request.completed": { "response": { "guidance": "Responde exactamente: Perfecto, te esperamos en Cra. 12 #16B-06, barrio Loperena, Valledupar, para revisar el equipo y terminar la compra. Atendemos de lunes a viernes de 8:00 a. m. a 12:00 m. y de 2:00 p. m. a 6:00 p. m.; los sabados de 8:00 a. m. a 12:00 m." } },
            "request.confirmation_required": { "response": { "guidance": "Pregunta si desea ir al local para terminar la compra." } }
          }
        }],        "conversationGuidance": "Continua como una vendedora real, cercana y resolutiva. Responde primero a lo que la persona pregunto, usa palabras cotidianas y, cuando aporte valor, da una recomendacion breve en primera persona sustentada en el catalogo. No repitas la misma entrada, elogio ni cierre. Estructura comparaciones en listas cortas y no cierres cada respuesta con una pregunta. Si el turno pregunta precio, disponibilidad o condicion y no existe un outcome autoritativo exitoso del catalogo en ese mismo turno, no cites ningun precio, no reutilices uno del historial y no afirmes que el modelo esta agotado o no disponible. Para bateria usada informa mas del 90% y revision exacta en tienda. Para equipos nuevos, garantia directamente con la marca. Si pregunta por servicio tecnico, invita a acercarse al local. Si quiere comprar o pagar, activa store_visit_confirmed.",
        "awaitCustomerReply": true
      }
    ]
  }],
  "globalActions": [
        {
      "id": "store_location",
      "priority": 125,
      "goal": "Dar la direccion y el horario reales del local para compras, visitas o consultas generales.",
      "conversationGuidance": "Responde con la ubicacion y el horario configurados. Una pregunta general por donde queda la tienda, direccion, ubicacion u horario no es servicio tecnico. No preguntes por modelo ni condicion.",
      "signal": {
        "type": "store_location_requested",
        "description": "El cliente pregunta donde queda Digital Shop, cual es la direccion, donde puede comprar o visitar, o cual es el horario. No implica servicio tecnico salvo que tambien solicite reparar o revisar un equipo.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [],
      "response": { "template": "store_location" }
    },
    {
      "id": "purchase_interest_store_visit",
      "priority": 124,
      "goal": "Orientar al local cuando el cliente ya muestra interes claro en comprar.",
      "conversationGuidance": "Confirma brevemente que puede completar la compra en el local y muestra direccion y horario una sola vez. No repitas la ficha del telefono.",
      "signal": {
        "type": "purchase_interest_expressed",
        "description": "El cliente expresa que quiere comprar, separar, llevar, pasar por el equipo, pregunta como finalizar la compra o dice claramente que le interesa adquirirlo. No aplica a una simple consulta de precio, caracteristicas, disponibilidad o comparacion.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [],
      "response": { "template": "store_location" }
    },
    {
      "id": "technical_service",
      "priority": 120,
      "goal": "Responder consultas de servicio tecnico desde cualquier etapa.",
      "conversationGuidance": "Cuando el cliente solicite reparar, revisar o dar mantenimiento a un equipo, dirige al local con la direccion y horario configurados. No preguntes por modelo, nuevo o usado.",
      "signal": {
        "type": "technical_service_requested",
        "description": "El cliente solicita explicitamente reparacion, revision, mantenimiento, cambio de pieza o servicio tecnico para un equipo. No emitas esta senal por una pregunta general de ubicacion, direccion, visita, compra u horario.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [],
      "response": { "template": "technical_service_local" }
    },
        {
      "id": "iphone_catalog_sales",
      "priority": 115,
      "goal": "Mostrar directamente todos los modelos iPhone activos cuando el cliente pregunta qué teléfonos vende la tienda.",
      "conversationGuidance": "Presenta el listado autoritativo de modelos iPhone sin ofrecer otras marcas, sin preguntar por categoria y sin inventar referencias.",
      "signal": {
        "type": "iphone_catalog_requested",
        "description": "El cliente solicita conocer en general que telefonos, celulares, equipos o modelos vende la tienda y no ha indicado un modelo especifico.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [{
        "id": "list_iphone_models",
        "operation": "commerce.search_products",
        "trigger": "on_signal",
        "signal": "iphone_catalog_requested",
        "arguments": { "query": "iPhone", "mode": "search_target", "limit": 50, "include_stock": false },
        "execution": { "idempotency": "input_version", "maxAttempts": 1 },
        "onOutcome": {
          "products.found": { "effects": [{ "type": "presentation.add", "template": "iphone_model_catalog", "mode": "Exclusive", "priority": "Required" }] },
          "products.not_found": { "response": { "guidance": "Indica brevemente que el catalogo no devolvio modelos iPhone activos. No ofrezcas otras marcas." } },
          "catalog.not_ready": { "response": { "guidance": "Indica brevemente que el catalogo de iPhone no esta disponible temporalmente." } },
          "products.search_failed": { "response": { "guidance": "Indica brevemente que no fue posible consultar los modelos iPhone en este momento." } }
        }
      }]
    },    {
      "id": "accessory_sales",
      "priority": 110,
      "goal": "Buscar y mostrar directamente accesorios como cargadores, cubos o cables.",
      "conversationGuidance": "Consulta el accesorio solicitado y muestra el resultado estructurado sin preguntar nuevo o usado ni pedir permiso para mostrarlo.",
      "signal": {
        "type": "accessory_product_requested",
        "description": "El cliente quiere comprar, pregunta precio, disponibilidad o informacion de un accesorio como cargador, cubo, adaptador o cable. No aplica a un iPhone.",
        "valueSchema": {
          "type": "object",
          "properties": { "product_query": { "type": "string", "minLength": 1 } },
          "required": ["product_query"],
          "additionalProperties": false
        }
      },
      "actions": [{
        "id": "search_accessory",
        "operation": "commerce.search_product_offers",
        "trigger": "on_signal",
        "signal": "accessory_product_requested",
        "arguments": {
          "product_query": "{{signal.accessory_product_requested.value.product_query}}",
          "condition": "new"
        },
        "execution": { "idempotency": "input_version", "maxAttempts": 1 },
        "onOutcome": {
          "offers.found": {
            "effects": [{
              "type": "presentation.add",
              "template": "accessory_product_offers",
              "mode": "Exclusive",
              "priority": "Required"
            }]
          },
          "offers.not_found": {
            "response": {
              "guidance": "Indica en dos lineas que ese accesorio no tiene una oferta vigente en el catalogo y que puede consultar otra referencia. No inventes disponibilidad."
            }
          }
        }
      }]
    },
    {
      "id": "switch_used_to_new",
      "priority": 118,
      "goal": "Mostrar la oferta nueva del modelo actual cuando el cliente cambia desde una oferta usada hacia nuevo.",
      "conversationGuidance": "Presenta la oferta nueva solicitada y una comparacion comercial corta con la condicion usada anterior: nuevo significa estrenar y garantia de marca; usado prioriza ahorro y bateria superior al 90%. No repitas la ficha tecnica ni la direccion ya presentada.",
      "signal": {
        "type": "new_condition_offer_requested",
        "description": "La condicion vigente del modelo era usado y el cliente solicita ahora ese mismo modelo nuevo, su precio nuevo o la alternativa nueva. Es un cambio de condicion. No emitas esta senal si el mensaje pide comparar nuevo y usado o menciona ambas condiciones como alternativas.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [
        {
          "id": "switch_show_new_offer",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "new_condition_offer_requested",
          "condition": { "all": [{ "factPresent": "device_model" }, { "factEquals": { "key": "product_condition", "value": "new" } }] },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [
              { "type": "fact.set", "fact": "product_condition", "value": "new" },
              { "type": "fact.set", "fact": "offer_presented", "value": true },
              { "type": "presentation.add", "template": "switched_new_offer", "mode": "Exclusive", "priority": "Required" }
            ] },
            "offers.not_found": { "response": { "guidance": "Indica que no hay oferta nueva vigente para el modelo actual." } }
          }
        }
      ]
    },
    {
      "id": "switch_new_to_used",
      "priority": 118,
      "goal": "Mostrar la oferta usada del modelo actual cuando el cliente cambia desde una oferta nueva hacia usado.",
      "conversationGuidance": "Presenta la oferta usada solicitada y una comparacion comercial corta con la condicion nueva anterior: usado prioriza ahorro y bateria superior al 90%; nuevo significa estrenar y garantia de marca. No repitas la ficha tecnica ni la direccion ya presentada.",
      "signal": {
        "type": "used_condition_offer_requested",
        "description": "La condicion vigente del modelo era nuevo y el cliente solicita ahora ese mismo modelo usado, su precio usado o la alternativa usada. Es un cambio de condicion. No emitas esta senal si el mensaje pide comparar nuevo y usado o menciona ambas condiciones como alternativas.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [
        {
          "id": "switch_show_used_offer",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "used_condition_offer_requested",
          "condition": { "all": [{ "factPresent": "device_model" }, { "factEquals": { "key": "product_condition", "value": "used" } }] },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [
              { "type": "fact.set", "fact": "product_condition", "value": "used" },
              { "type": "fact.set", "fact": "offer_presented", "value": true },
              { "type": "presentation.add", "template": "switched_used_offer", "mode": "Exclusive", "priority": "Required" }
            ] },
            "offers.not_found": { "response": { "guidance": "Indica que no hay oferta usada vigente para el modelo actual." } }
          }
        }
      ]
    },    {
      "id": "compare_two_iphone_models",
      "priority": 119,
      "goal": "Comparar tecnicamente dos modelos iPhone distintos y recomendar el mas conveniente con evidencia del catalogo.",
      "conversationGuidance": "Compara exclusivamente los dos modelos nombrados. Modelo contra modelo y nunca nuevo contra usado. Usa pantalla y tamaño, camaras y autonomia solo cuando aparezcan en las descripciones autoritativas. Di cual es mejor para el uso que se desprenda del mensaje; si no hay un uso indicado, recomienda el tecnicamente mas completo y explica dos motivos concretos. Si existe una condicion vigente, los dos precios deben corresponder a esa misma condicion. Si no existe, entrega primero la comparacion tecnica y pregunta de forma natural si los esta buscando nuevos o usados.",
      "signal": {
        "type": "two_iphone_models_comparison_requested",
        "description": "El cliente pide comparar, conocer diferencias o decidir entre dos modelos iPhone distintos. Puede nombrar ambos en el mensaje, por ejemplo iPhone 16 y iPhone 17, o nombrar uno y referirse claramente al modelo cotizado inmediatamente antes. Extrae exactamente los dos modelos completos. Esta senal tiene prioridad sobre compare_new_and_used y nunca representa una comparacion de condiciones.",
        "valueSchema": {
          "type": "object",
          "properties": {
            "model_a": { "type": "string", "minLength": 1 },
            "model_b": { "type": "string", "minLength": 1 }
          },
          "required": ["model_a", "model_b"],
          "additionalProperties": false
        }
      },
      "actions": [
        {
          "id": "load_first_model_specs",
          "operation": "commerce.search_products",
          "trigger": "on_signal",
          "signal": "two_iphone_models_comparison_requested",
          "arguments": { "query": "{{signal.two_iphone_models_comparison_requested.value.model_a}}", "mode": "search_target", "limit": 1, "include_stock": false },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "products.found": { "effects": [{ "type": "presentation.add", "template": "technical_comparison_model", "mode": "Inline", "priority": "Required" }] },
            "products.not_found": { "response": { "guidance": "Indica que el primer modelo no aparece en el catalogo tecnico y no inventes sus especificaciones." } },
            "catalog.not_ready": { "response": { "guidance": "Indica que el catalogo tecnico no esta disponible temporalmente." } },
            "products.search_failed": { "response": { "guidance": "Indica que no fue posible consultar el primer modelo." } }
          }
        },
        {
          "id": "load_second_model_specs",
          "operation": "commerce.search_products",
          "trigger": "on_signal",
          "signal": "two_iphone_models_comparison_requested",
          "arguments": { "query": "{{signal.two_iphone_models_comparison_requested.value.model_b}}", "mode": "search_target", "limit": 1, "include_stock": false },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "products.found": { "effects": [{ "type": "presentation.add", "template": "technical_comparison_model", "mode": "Inline", "priority": "Required" }] },
            "products.not_found": { "response": { "guidance": "Indica que el segundo modelo no aparece en el catalogo tecnico y no inventes sus especificaciones." } },
            "catalog.not_ready": { "response": { "guidance": "Indica que el catalogo tecnico no esta disponible temporalmente." } },
            "products.search_failed": { "response": { "guidance": "Indica que no fue posible consultar el segundo modelo." } }
          }
        },
        {
          "id": "compare_first_new_price",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "two_iphone_models_comparison_requested",
          "condition": { "factEquals": { "key": "product_condition", "value": "new" } },
          "arguments": { "product_query": "{{signal.two_iphone_models_comparison_requested.value.model_a}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [{ "type": "presentation.add", "template": "technical_comparison_new_price", "mode": "Inline", "priority": "Required" }] }, "offers.not_found": { "response": { "guidance": "Indica que el primer modelo no tiene oferta nueva vigente." } } }
        },
        {
          "id": "compare_second_new_price",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "two_iphone_models_comparison_requested",
          "condition": { "factEquals": { "key": "product_condition", "value": "new" } },
          "arguments": { "product_query": "{{signal.two_iphone_models_comparison_requested.value.model_b}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [{ "type": "presentation.add", "template": "technical_comparison_new_price", "mode": "Inline", "priority": "Required" }] }, "offers.not_found": { "response": { "guidance": "Indica que el segundo modelo no tiene oferta nueva vigente." } } }
        },
        {
          "id": "compare_first_used_price",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "two_iphone_models_comparison_requested",
          "condition": { "factEquals": { "key": "product_condition", "value": "used" } },
          "arguments": { "product_query": "{{signal.two_iphone_models_comparison_requested.value.model_a}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [{ "type": "presentation.add", "template": "technical_comparison_used_price", "mode": "Inline", "priority": "Required" }] }, "offers.not_found": { "response": { "guidance": "Indica que el primer modelo no tiene oferta usada vigente." } } }
        },
        {
          "id": "compare_second_used_price",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "two_iphone_models_comparison_requested",
          "condition": { "factEquals": { "key": "product_condition", "value": "used" } },
          "arguments": { "product_query": "{{signal.two_iphone_models_comparison_requested.value.model_b}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [{ "type": "presentation.add", "template": "technical_comparison_used_price", "mode": "Inline", "priority": "Required" }] }, "offers.not_found": { "response": { "guidance": "Indica que el segundo modelo no tiene oferta usada vigente." } } }
        }
      ],
      "response": { "guidance": "Empieza con una conclusion clara y humana. Luego muestra Diferencias clave en una lista breve y cierra con una recomendacion natural, explicando cual elegirias y por que con dos especificaciones autoritativas, sin usar un encabezado o frase fija. Nunca conviertas la comparacion en nuevo contra usado. Si no hay bloques de precio, cierra con una sola pregunta natural: Para aterrizarte los precios de ambos, ¿los estas buscando nuevos o usados?" }
    },    {
      "id": "compare_new_and_used",
      "priority": 100,
      "goal": "Comparar el mismo modelo en nuevo y usado cuando el cliente pide expresamente una comparacion o diferencias.",
      "conversationGuidance": "Presenta una comparacion breve y estructurada con los dos resultados del catalogo. Incluye precio, capacidad, garantia de nuevo y bateria de usado cuando apliquen. Si una condicion no tiene oferta vigente, dilo expresamente. No preguntes si quiere que hagas la comparacion y no cierres con otra pregunta.",
      "signal": {
        "type": "compare_new_and_used",
        "description": "El cliente menciona un solo modelo y pide expresamente compararlo en las dos condiciones: nuevo y usado, conocer ambos precios o entender la diferencia comercial entre ambas condiciones. Nunca emitas esta senal si el mensaje contiene dos modelos distintos, aunque no indique condicion. No aplica a un seguimiento breve que solo cambia a la condicion contraria.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [
        {
          "id": "compare_new_offer",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "compare_new_and_used",
          "condition": { "factPresent": "device_model" },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [{ "type": "presentation.add", "template": "condition_comparison_new", "mode": "Exclusive", "priority": "Required" }] },
            "offers.not_found": { "response": { "guidance": "En la comparacion indica: Nuevo: sin oferta vigente en el catalogo." } }
          }
        },
        {
          "id": "compare_used_offer",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "compare_new_and_used",
          "condition": { "factPresent": "device_model" },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [{ "type": "presentation.add", "template": "condition_comparison_used", "mode": "Exclusive", "priority": "Required" }] },
            "offers.not_found": { "response": { "guidance": "En la comparacion indica: Usado: sin oferta vigente en el catalogo." } }
          }
        }
      ]
    },
    {
      "id": "compare_different_iphone_models",
      "priority": 118,
      "goal": "Consultar siempre cualquier modelo iPhone distinto solicitado y, solo la primera vez, compararlo con el modelo anterior.",
      "conversationGuidance": "Presenta los dos modelos bajo la misma condicion. Resume solo diferencias principales de pantalla y tamaño, camaras y autonomia presentes en las descripciones autoritativas. Termina con una recomendacion honesta: indica cual es tecnicamente mejor y por que; si el anterior solo conviene por precio, tamaño o ahorro, dilo claramente. Esta comparacion automatica ocurre una sola vez.",
      "signal": {
        "type": "different_model_requested",
        "description": "El cliente solicita un iPhone distinto al modelo cotizado inmediatamente antes, sin pedir una comparacion expresa, y en el mismo mensaje indica explicitamente si ese nuevo modelo lo quiere nuevo o usado. Si cambia de modelo sin decir la condicion, no emitas esta senal: product_condition se invalida y la etapa discover vuelve a preguntarla. Emite esta misma senal tanto para el segundo modelo como para modelos posteriores cuando la condicion si viene explicita. Usa el historial reciente: current_product_query es el modelo nuevo completo, normalizado siempre con la palabra iPhone, y previous_product_query es el modelo cotizado inmediatamente antes. No aplica a cambios nuevo/usado del mismo modelo, colores ni bateria.",
        "valueSchema": {
          "type": "object",
          "properties": {
            "current_product_query": { "type": "string", "minLength": 1 },
            "previous_product_query": { "type": "string", "minLength": 1 }
          },
          "required": ["current_product_query", "previous_product_query"],
          "additionalProperties": false
        }
      },
      "actions": [
        {
          "id": "compare_current_new_model",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "different_model_requested",
          "condition": { "all": [{ "factMissing": "automatic_model_comparison_used" }, { "factEquals": { "key": "product_condition", "value": "new" } }] },
          "arguments": { "product_query": "{{signal.different_model_requested.value.current_product_query}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [
            { "type": "fact.set", "fact": "offer_presented", "value": true },

            { "type": "presentation.add", "template": "compared_current_new_offer", "mode": "Inline", "priority": "Required" }
          ] }, "offers.not_found": { "response": { "guidance": "Indica que el nuevo modelo solicitado no tiene oferta nueva vigente." } } }
        },
        {
          "id": "compare_previous_new_model",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "different_model_requested",
          "condition": { "all": [{ "factMissing": "automatic_model_comparison_used" }, { "factEquals": { "key": "product_condition", "value": "new" } }] },
          "arguments": { "product_query": "{{signal.different_model_requested.value.previous_product_query}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [{ "type": "fact.set", "fact": "automatic_model_comparison_used", "value": true }, { "type": "presentation.add", "template": "compared_previous_new_offer", "mode": "Inline", "priority": "Required" }] }, "offers.not_found": { "response": { "guidance": "Indica que el modelo anterior no tiene oferta nueva vigente para compararlo." } } }
        },
        {
          "id": "compare_current_used_model",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "different_model_requested",
          "condition": { "all": [{ "factMissing": "automatic_model_comparison_used" }, { "factEquals": { "key": "product_condition", "value": "used" } }] },
          "arguments": { "product_query": "{{signal.different_model_requested.value.current_product_query}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [
            { "type": "fact.set", "fact": "offer_presented", "value": true },

            { "type": "presentation.add", "template": "compared_current_used_offer", "mode": "Inline", "priority": "Required" }
          ] }, "offers.not_found": { "response": { "guidance": "Indica que el nuevo modelo solicitado no tiene oferta usada vigente." } } }
        },
        {
          "id": "compare_previous_used_model",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "different_model_requested",
          "condition": { "all": [{ "factMissing": "automatic_model_comparison_used" }, { "factEquals": { "key": "product_condition", "value": "used" } }] },
          "arguments": { "product_query": "{{signal.different_model_requested.value.previous_product_query}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [{ "type": "fact.set", "fact": "automatic_model_comparison_used", "value": true }, { "type": "presentation.add", "template": "compared_previous_used_offer", "mode": "Inline", "priority": "Required" }] }, "offers.not_found": { "response": { "guidance": "Indica que el modelo anterior no tiene oferta usada vigente para compararlo." } } }
        },
        {
          "id": "show_followup_new_model",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "different_model_requested",
          "condition": { "all": [{ "factPresent": "automatic_model_comparison_used" }, { "factEquals": { "key": "product_condition", "value": "new" } }] },
          "arguments": { "product_query": "{{signal.different_model_requested.value.current_product_query}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [
            { "type": "fact.set", "fact": "offer_presented", "value": true },
            { "type": "presentation.add", "template": "additional_new_offer", "mode": "Exclusive", "priority": "Required" }
          ] }, "offers.not_found": { "response": { "guidance": "Indica que el modelo consultado no tiene oferta nueva vigente solo despues de este resultado autoritativo." } } }
        },
        {
          "id": "show_followup_used_model",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "different_model_requested",
          "condition": { "all": [{ "factPresent": "automatic_model_comparison_used" }, { "factEquals": { "key": "product_condition", "value": "used" } }] },
          "arguments": { "product_query": "{{signal.different_model_requested.value.current_product_query}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": { "offers.found": { "effects": [
            { "type": "fact.set", "fact": "offer_presented", "value": true },
            { "type": "presentation.add", "template": "additional_used_offer", "mode": "Exclusive", "priority": "Required" }
          ] }, "offers.not_found": { "response": { "guidance": "Indica que el modelo consultado no tiene oferta usada vigente solo despues de este resultado autoritativo." } } }
        }
      ],
      "response": { "guidance": "Si hay dos bloques autoritativos, no reconstruyas ni repitas ninguno: agrega solamente una lista breve titulada Diferencias clave, con maximo tres diferencias principales, y una recomendacion honesta final basada en ellas. No vuelvas a escribir nombres, precios, capacidades ni fichas ya presentadas. Si hay un solo bloque, no lo repitas ni agregues elogios genericos. Nunca cierres con si quieres, preguntas de ofrecimiento o invitaciones al local sin interes de compra. Nunca afirmes que un producto no existe sin un outcome offers.not_found y no muestres la direccion hasta que el cliente exprese interes de compra o la solicite." }
    },    {
      "id": "show_product_colors",
      "priority": 95,
      "goal": "Responder solo los colores vigentes del producto consultado sin repetir la cotizacion completa.",
      "conversationGuidance": "Muestra unicamente los colores autoritativos del catalogo para el modelo y condicion actuales. No repitas precio, bateria, garantia ni la cotizacion completa. No cierres con una pregunta.",
      "signal": {
        "type": "product_colors_requested",
        "description": "El cliente pregunta especificamente que color o colores hay disponibles para el modelo actual. No aplica a una consulta general de detalles, precio, capacidad, imagen o garantia.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [
        {
          "id": "show_new_colors",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "product_colors_requested",
          "condition": { "all": [
            { "factPresent": "device_model" },
            { "factEquals": { "key": "product_condition", "value": "new" } }
          ] },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [{ "type": "presentation.add", "template": "product_color_options", "mode": "Exclusive", "priority": "Required" }] },
            "offers.not_found": { "response": { "guidance": "Indica brevemente que no hay colores confirmados para una oferta nueva de ese modelo." } }
          }
        },
        {
          "id": "show_used_colors",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "product_colors_requested",
          "condition": { "all": [
            { "factPresent": "device_model" },
            { "factEquals": { "key": "product_condition", "value": "used" } }
          ] },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [{ "type": "presentation.add", "template": "product_color_options", "mode": "Exclusive", "priority": "Required" }] },
            "offers.not_found": { "response": { "guidance": "Indica brevemente que no hay colores confirmados para una oferta usada de ese modelo." } }
          }
        }
      ]
    },    {
      "id": "show_product_details",
      "priority": 90,
      "goal": "Mostrar automaticamente imagenes y datos vigentes del modelo cuando el cliente pregunta por precio, capacidad, color, imagen o garantia.",
      "conversationGuidance": "Muestra directamente lo que devuelva el catalogo, sin preguntar si el cliente quiere verlo. Si el catalogo no trae color, di que el color no esta confirmado en el inventario cargado. No inventes datos ni cierres con una pregunta.",
      "signal": {
        "type": "show_product_details",
        "description": "El cliente pregunta por imagenes, fotos, precio, capacidad, disponibilidad o garantia sin cambiar la condicion vigente del modelo. No aplica cuando solicita la condicion contraria, ni a colores ni a una comparacion de nuevo y usado.",
        "valueSchema": { "type": "object", "properties": { "requested": { "type": "boolean", "const": true } }, "required": ["requested"], "additionalProperties": false }
      },
      "actions": [
        {
          "id": "show_new_details",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "show_product_details",
          "condition": { "all": [
            { "factPresent": "device_model" },
            { "factEquals": { "key": "product_condition", "value": "new" } }
          ] },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "new" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [{ "type": "presentation.add", "template": "new_product_offers", "mode": "Exclusive", "priority": "Required" }] },
            "offers.not_found": { "response": { "guidance": "Indica que no hay una oferta nueva vigente para ese modelo; no inventes colores, imagenes ni precio." } }
          }
        },
        {
          "id": "show_used_details",
          "operation": "commerce.search_product_offers",
          "trigger": "on_signal",
          "signal": "show_product_details",
          "condition": { "all": [
            { "factPresent": "device_model" },
            { "factEquals": { "key": "product_condition", "value": "used" } }
          ] },
          "arguments": { "product_query": "{{fact.device_model}}", "condition": "used" },
          "execution": { "idempotency": "input_version", "maxAttempts": 1 },
          "onOutcome": {
            "offers.found": { "effects": [{ "type": "presentation.add", "template": "used_product_offers", "mode": "Exclusive", "priority": "Required" }] },
            "offers.not_found": { "response": { "guidance": "Indica que no hay una oferta usada vigente para ese modelo; no inventes colores, imagenes ni precio." } }
          }
        }
      ]
    }
  ],
  "messageSequences": {},
  "notifications": {},
  "escalations": { "human": { "contacts": [] } },
  "checkout": { "currency": "COP", "modes": {} },
  "commerce": { "enabled": true, "provider": "Local" },
  "operatingHours": { "enforce": false }
}';

IF ISJSON(@SettingsJson) <> 1
    THROW 51000, 'SeedDigitalShop: SettingsJson invalido.', 1;

MERGE dbo.Agents AS target
USING (SELECT @AgentId AgentId) AS source
ON target.AgentId = source.AgentId
WHEN MATCHED THEN UPDATE SET
    BusinessId = @BusinessId,
    AgentTypeId = @AgentTypeId,
    [Name] = N'Asesor Digital Shop',
    [Description] = N'Agente de venta informativa de iPhone nuevos y usados.',
    Kind = N'customer',
    BotType = 2,
    IsActive = 1,
    SettingsJson = @SettingsJson,
    Model = N'gpt-4.1-mini',
    Temperature = 0.2,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (AgentId, BusinessId, AgentTypeId, [Name], [Description], Kind, BotType,
     IsActive, SettingsJson, Model, Temperature, CreatedAt)
VALUES
    (@AgentId, @BusinessId, @AgentTypeId, N'Asesor Digital Shop',
     N'Agente de venta informativa de iPhone nuevos y usados.', N'customer', 2,
     1, @SettingsJson, N'gpt-4.1-mini', 0.2, SYSUTCDATETIME());

DECLARE @OperationsSettingsJson NVARCHAR(MAX) = N'{
  "model": "gpt-4.1-mini",
  "temperature": 0.0,
  "historyWindowSize": 8,
  "extractorHistoryWindowSize": 1,
  "persona": "Eres el bot interno de operaciones de Digital Shop. Recibes listas de precios escritas, en PDF o en imagen y aplicas actualizaciones conservadoras al catalogo.",
  "policies": "Nunca inventes precios ni modelos. Solo actualiza filas que incluyan modelo iPhone, condicion nuevo o usado y un precio. Si falta la condicion global o por fila, pide corregir la lista. Reporta siempre cuantas ofertas cambiaron y cuales requieren revision.",
  "conversationOpening": { "enabled": true, "allowQuestions": true, "guidance": "Pide enviar o pegar la lista de precios. Aceptas texto, PDF o imagen." },
  "failureResponses": { "llmUnavailable": "No pude procesar la lista. Intenta enviarla nuevamente como texto, PDF o imagen legible." },
  "conversationFollowUp": { "enabled": true, "delayMinutes": 60, "respectOperatingHours": false, "guidance": "Pregunta si desea enviar otra lista o corregir las filas rechazadas." },
  "factSchema": [{
    "key": "price_list_text",
    "role": "catalog.price_list_text",
    "label": "lista de precios",
    "type": "string",
    "source": "user",
    "scope": "request",
    "extractionGuidance": "Copia literalmente la lista completa enviada o el texto que siga a Texto extraido. Conserva saltos de linea, encabezados Nuevo/Usado, modelo, capacidad y precio. No resumas."
  }],
  "flows": [{
    "id": "price_list_updates",
    "type": "primary",
    "routingGuidance": "Actualizacion operativa del catalogo desde texto escrito, PDF o imagen.",
    "stages": [{
      "id": "receive_and_apply",
      "name": "Recibir y actualizar precios",
      "goal": "Aplicar una lista legible y devolver un resumen auditable.",
      "collect": ["price_list_text"],
      "reentryOnFactChanged": ["price_list_text"],
      "actions": [{
        "id": "update_prices",
        "operation": "internal.update_product_offer_prices",
        "trigger": "when_ready",
        "condition": { "all": [{ "factPresent": "price_list_text" }] },
        "arguments": {
          "price_list_text": "{{fact.price_list_text}}",
          "source": null
        },
        "execution": { "idempotency": "input_version", "maxAttempts": 1 },
        "onOutcome": {
          "prices.updated": {
            "response": { "guidance": "Confirma el total actualizado y presenta brevemente modelo, condicion, capacidad y nuevo precio." }
          },
          "prices.review_required": {
            "response": { "guidance": "Informa cuantos se actualizaron y enumera las filas rechazadas con su motivo. Pide reenviar solo esas filas corregidas." }
          },
          "prices.no_changes": {
            "response": { "mode": "ask_clarification", "guidance": "No se cambio ningun precio. Explica que cada fila necesita modelo, nuevo/usado y precio; capacidad cuando haya mas de una." }
          }
        }
      }],
      "awaitCustomerReply": true
    }]
  }],
  "globalActions": [],
  "messageSequences": {},
  "notifications": {},
  "escalations": { "human": { "contacts": [] } },
  "checkout": { "currency": "COP", "modes": {} },
  "commerce": { "enabled": false, "provider": "Local" },
  "operatingHours": { "enforce": false }
}';

IF ISJSON(@OperationsSettingsJson) <> 1
    THROW 51000, 'SeedDigitalShop: OperationsSettingsJson invalido.', 1;

MERGE dbo.Agents AS target
USING (SELECT @OperationsAgentId AgentId) AS source
ON target.AgentId = source.AgentId
WHEN MATCHED THEN UPDATE SET
    BusinessId = @BusinessId,
    AgentTypeId = @AgentTypeId,
    [Name] = N'Operaciones Digital Shop',
    [Description] = N'Actualiza ofertas desde listas de precios por texto, PDF o imagen.',
    Kind = N'internal',
    BotType = 3,
    IsActive = 1,
    SettingsJson = @OperationsSettingsJson,
    Model = N'gpt-4.1-mini',
    Temperature = 0.0,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (AgentId, BusinessId, AgentTypeId, [Name], [Description], Kind, BotType,
     IsActive, SettingsJson, Model, Temperature, CreatedAt)
VALUES
    (@OperationsAgentId, @BusinessId, @AgentTypeId, N'Operaciones Digital Shop',
     N'Actualiza ofertas desde listas de precios por texto, PDF o imagen.',
     N'internal', 3, 1, @OperationsSettingsJson, N'gpt-4.1-mini', 0.0,
     SYSUTCDATETIME());

SELECT @PlanId = SubscriptionPlanId
FROM dbo.SubscriptionPlans
WHERE Code = N'essential' AND IsActive = 1;
IF @PlanId IS NULL
    SELECT TOP (1) @PlanId = SubscriptionPlanId FROM dbo.SubscriptionPlans WHERE IsActive = 1 ORDER BY MonthlyPriceCop, CreatedAt;
IF @PlanId IS NULL
    THROW 51000, 'SeedDigitalShop: no existe un plan activo.', 1;

MERGE dbo.BusinessSubscriptions AS target
USING (
    SELECT
        @SubscriptionId BusinessSubscriptionId,
        p.SubscriptionPlanId,
        p.Code,
        p.[Name],
        p.MonthlyPriceCop,
        p.IncludedCredits,
        p.MaxVariableCostCop,
        p.MaxVariableCostPercent
    FROM dbo.SubscriptionPlans p WHERE p.SubscriptionPlanId = @PlanId
) AS source
ON target.BusinessSubscriptionId = source.BusinessSubscriptionId
WHEN MATCHED THEN UPDATE SET
    SubscriptionPlanId = source.SubscriptionPlanId,
    Status = 1,
    CurrentPeriodStart = DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1),
    CurrentPeriodEnd = DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)),
    PlanCodeSnapshot = source.Code,
    PlanNameSnapshot = source.[Name],
    MonthlyPriceCop = source.MonthlyPriceCop,
    IncludedCredits = source.IncludedCredits,
    MaxVariableCostCop = source.MaxVariableCostCop,
    MaxVariableCostPercent = source.MaxVariableCostPercent,
    ExtraCredits = 0,
    ExtraVariableCostCop = 0,
    AutoRenew = 1,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (BusinessSubscriptionId, BusinessId, SubscriptionPlanId, Status, CurrentPeriodStart, CurrentPeriodEnd,
     PlanCodeSnapshot, PlanNameSnapshot, MonthlyPriceCop, IncludedCredits, MaxVariableCostCop,
     MaxVariableCostPercent, ExtraCredits, ExtraVariableCostCop, AutoRenew, CreatedAt, UpdatedAt)
VALUES
    (@SubscriptionId, @BusinessId, source.SubscriptionPlanId, 1,
     DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1),
     DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)),
     source.Code, source.[Name], source.MonthlyPriceCop, source.IncludedCredits,
     source.MaxVariableCostCop, source.MaxVariableCostPercent, 0, 0, 1,
     SYSUTCDATETIME(), SYSUTCDATETIME());

UPDATE dbo.BusinessSubscriptions
SET Status = 4, UpdatedAt = SYSUTCDATETIME()
WHERE BusinessId = @BusinessId
  AND BusinessSubscriptionId <> @SubscriptionId
  AND Status IN (1, 2, 3);

IF NOT EXISTS (
    SELECT 1 FROM dbo.BusinessUsagePeriods
    WHERE BusinessSubscriptionId = @SubscriptionId
      AND PeriodStart = DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1))
BEGIN
    INSERT INTO dbo.BusinessUsagePeriods
        (BusinessSubscriptionId, BusinessId, PeriodStart, PeriodEnd, CreditsIncluded,
         CreditsExtra, CreditsUsed, VariableCostLimitCop, VariableCostExtraCop,
         VariableCostUsedCop, Status, CreatedAt, UpdatedAt)
    SELECT
        @SubscriptionId, @BusinessId,
        DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1),
        DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)),
        IncludedCredits, 0, 0, MaxVariableCostCop, 0, 0, 1,
        SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM dbo.SubscriptionPlans WHERE SubscriptionPlanId = @PlanId;
END;

IF (SELECT COUNT(*) FROM dbo.BusinessSubscriptions WHERE BusinessId = @BusinessId AND Status IN (1, 2, 3)) <> 1
    THROW 51000, 'SeedDigitalShop: debe existir exactamente una suscripcion activa.', 1;

PRINT N'SeedDigitalShop: negocio, agente, 29 modelos, ofertas e imagenes configurados.';
GO
