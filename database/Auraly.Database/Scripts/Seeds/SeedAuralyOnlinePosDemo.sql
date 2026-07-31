/*
  Development-only, idempotent seed for the local Auraly POS visual environment.
  This file is intentionally not included in post-deployment.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @BusinessId UNIQUEIDENTIFIER = 'A0A10000-0000-0000-0000-000000000001';
DECLARE @WarehouseId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000002';
DECLARE @RegisterId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000003';
DECLARE @FiscalAuthorizationId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000004';
DECLARE @FiscalIssuerConfigurationId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000005';
DECLARE @DocumentSeriesId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000006';
DECLARE @FiscalSeriesId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000007';
DECLARE @TaxProfileId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000008';
DECLARE @ProductId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000009';
DECLARE @ProductBarcodeId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-00000000000A';
DECLARE @ProductPriceId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-00000000000B';
DECLARE @UserId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-000000000010';
DECLARE @OrderOneId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-00000000000C';
DECLARE @OrderTwoId UNIQUEIDENTIFIER = 'A0A10001-0000-7000-8000-00000000000D';
DECLARE @Now DATETIMEOFFSET(7) = SYSDATETIMEOFFSET();

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
    THROW 50001, 'The seeded AURALY business is missing. Deploy the DACPAC with its post-deployment seeds first.', 1;

UPDATE dbo.Businesses SET IsActive = 1 WHERE BusinessId = @BusinessId;

IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId)
    INSERT dbo.AppUsers(
        UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
        FirstName,LastName,IsActive,CreatedAt)
    VALUES(
        @UserId,'A0A10000-0000-0000-0000-000000000000',
        N'auraly.visual.cashier',N'AURALY.VISUAL.CASHIER',
        N'cajero.auraly@auraly.local',N'CAJERO.AURALY@AURALY.LOCAL',
        N'Cajero',N'Auraly',1,SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.Warehouses WHERE WarehouseId = @WarehouseId)
    INSERT dbo.Warehouses(
        WarehouseId,BusinessId,Code,Name,
        AllowNegativeStockSales,IsActive,CreatedAt)
    VALUES(
        @WarehouseId,@BusinessId,N'PRINCIPAL',N'Bodega principal',
        1,1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.CashRegisters WHERE RegisterId = @RegisterId)
    INSERT dbo.CashRegisters(
        RegisterId,BusinessId,WarehouseId,Code,Name,IsActive,CreatedAt)
    VALUES(
        @RegisterId,@BusinessId,@WarehouseId,N'01',N'Caja 01',1,@Now);

IF NOT EXISTS (
    SELECT 1 FROM dbo.FiscalAuthorizations
    WHERE FiscalAuthorizationId = @FiscalAuthorizationId)
    INSERT dbo.FiscalAuthorizations(
        FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,
        Environment,QrValidationUrl,TechnicalKeyVersion,
        ValidFrom,ValidUntil,IsActive,CreatedAt)
    VALUES(
        @FiscalAuthorizationId,@BusinessId,N'AURALY-VISUAL-2026',N'900123456',
        2,N'https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=',
        N'visual-v1','2026-01-01','2028-12-31',1,@Now);

IF NOT EXISTS (
    SELECT 1 FROM dbo.FiscalIssuerConfigurations
    WHERE FiscalIssuerConfigurationId = @FiscalIssuerConfigurationId)
    INSERT dbo.FiscalIssuerConfigurations(
        FiscalIssuerConfigurationId,BusinessId,Version,SupplierTaxId,
        SupplierCheckDigit,LegalName,TradeName,TaxLevelCode,TaxSchemeId,
        TaxSchemeName,IdentificationTypeCode,AddressLine,CityCode,CityName,
        DepartmentCode,DepartmentName,CountryCode,CountryName,
        SoftwareIdentificationCode,SoftwarePinSecretReference,Environment,
        TestSetId,CertificateProvider,CertificateKeyReference,
        CertificateThumbprint,DianEndpoint,TechnicalAnnexVersion,
        GeneratorVersion,ValidFrom,IsActive,CreatedAt)
    VALUES(
        @FiscalIssuerConfigurationId,@BusinessId,1,N'900123456',N'7',
        N'AURALY',N'AURALY',N'R-99-PN',N'01',N'IVA',N'31',
        N'Calle de demostración 1',N'11001',N'Bogotá',N'11',
        N'Bogotá D.C.',N'CO',N'Colombia',N'auraly-visual',
        N'env://AURALY_VISUAL_SOFTWARE_PIN',2,
        '11111111-1111-1111-1111-111111111111',N'Test',N'Test',N'VISUAL',
        N'https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc',
        N'1.9',N'Auraly.Visual','2026-01-01',1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.DocumentSeries WHERE DocumentSeriesId = @DocumentSeriesId)
    INSERT dbo.DocumentSeries(
        DocumentSeriesId,BusinessId,RegisterId,DocumentType,
        Prefix,SeriesCode,Padding,RangeStart,RangeEnd,
        IsOfflineCapable,IsActive,CreatedAt)
    VALUES(
        @DocumentSeriesId,@BusinessId,@RegisterId,N'SalesInvoice',
        N'VTA',N'01',8,1,99999999,1,1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.FiscalSeries WHERE SeriesId = @FiscalSeriesId)
    INSERT dbo.FiscalSeries(
        SeriesId,BusinessId,RegisterId,FiscalAuthorizationId,DocumentType,
        Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
    VALUES(
        @FiscalSeriesId,@BusinessId,@RegisterId,@FiscalAuthorizationId,N'SalesInvoice',
        N'FE',1,99999999,1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId = @TaxProfileId)
    INSERT dbo.TaxProfiles(
        TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
    VALUES(@TaxProfileId,@BusinessId,N'01',N'IVA 19%',19,1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE ProductId = @ProductId)
    INSERT dbo.Products(
        ProductId,BusinessId,ProductCode,Reference,BaseUnitCode,TaxProfileId,
        Source,Sku,Name,UnitPrice,Currency,ManageStock,IsWeighable,
        IsActive,CreatedAt)
    VALUES(
        @ProductId,@BusinessId,N'DEMO-001',N'REF-DEMO-001',N'EA',@TaxProfileId,
        0,N'DEMO-001',N'Producto Auraly de demostración',12500,N'COP',1,0,
        1,SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.ProductBarcodes WHERE ProductBarcodeId = @ProductBarcodeId)
    INSERT dbo.ProductBarcodes(
        ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
    VALUES(
        @ProductBarcodeId,@BusinessId,@ProductId,N'7700000000001',1,1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.ProductPrices WHERE ProductPriceId = @ProductPriceId)
    INSERT dbo.ProductPrices(
        ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,
        ValidFrom,IsActive,CreatedAt)
    VALUES(
        @ProductPriceId,@BusinessId,@ProductId,12500,N'COP',
        DATEADD(day,-1,@Now),1,@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.CatalogChanges WHERE BusinessId=@BusinessId AND ProductId=@ProductId)
    INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
    VALUES(@BusinessId,@ProductId,N'Upsert',@Now);

IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId=@OrderOneId)
BEGIN
    INSERT dbo.Orders(
        OrderId,BusinessId,Source,FulfillmentMode,Status,
        CustomerNameSnapshot,CustomerPhoneSnapshot,CustomerDocumentSnapshot,
        Notes,Currency,Subtotal,DiscountTotal,Total,
        CustomerConfirmed,IdempotencyKey,CreatedAt)
    VALUES(
        @OrderOneId,@BusinessId,0,0,2,
        N'Laura Gómez',N'3001112233',N'1020304050',
        N'Pedido creado por el bot para demostración',N'COP',
        25000,0,25000,1,N'auraly-demo-order-1',DATEADD(minute,-18,SYSUTCDATETIME()));

    INSERT dbo.OrderItems(
        OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductNameSnapshot,
        Quantity,UnitPrice,DiscountAmount,LineTotal,CreatedAt)
    VALUES(
        'A0A10001-0000-7000-8000-00000000000E',@OrderOneId,@BusinessId,
        @ProductId,N'DEMO-001',N'Producto Auraly de demostración',
        2,12500,0,25000,SYSUTCDATETIME());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId=@OrderTwoId)
BEGIN
    INSERT dbo.Orders(
        OrderId,BusinessId,Source,FulfillmentMode,Status,
        CustomerNameSnapshot,CustomerPhoneSnapshot,CustomerDocumentSnapshot,
        Notes,Currency,Subtotal,DiscountTotal,Total,
        CustomerConfirmed,IdempotencyKey,CreatedAt)
    VALUES(
        @OrderTwoId,@BusinessId,0,0,2,
        N'Carlos Ruiz',N'3004445566',N'79845612',
        N'Segundo pedido del bot para facturación múltiple',N'COP',
        12500,0,12500,1,N'auraly-demo-order-2',DATEADD(minute,-6,SYSUTCDATETIME()));

    INSERT dbo.OrderItems(
        OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductNameSnapshot,
        Quantity,UnitPrice,DiscountAmount,LineTotal,CreatedAt)
    VALUES(
        'A0A10001-0000-7000-8000-00000000000F',@OrderTwoId,@BusinessId,
        @ProductId,N'DEMO-001',N'Producto Auraly de demostración',
        1,12500,0,12500,SYSUTCDATETIME());
END;

COMMIT TRANSACTION;

SELECT
    b.Name AS BusinessName,
    r.Name AS RegisterName,
    w.Name AS WarehouseName,
    p.Name AS ProductName,
    pb.Barcode,
    (SELECT COUNT(*) FROM dbo.Orders o WHERE o.BusinessId=@BusinessId AND o.Source=0) AS BotOrders
FROM dbo.Businesses b
JOIN dbo.CashRegisters r ON r.BusinessId=b.BusinessId
JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
JOIN dbo.Products p ON p.BusinessId=b.BusinessId AND p.ProductId=@ProductId
JOIN dbo.ProductBarcodes pb ON pb.ProductId=p.ProductId AND pb.IsActive=1
WHERE b.BusinessId=@BusinessId AND r.RegisterId=@RegisterId;
