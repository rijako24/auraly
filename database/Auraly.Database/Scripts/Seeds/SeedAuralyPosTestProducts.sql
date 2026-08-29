/* Development-only, idempotent products for the local online POS flow. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @TaxProfileId UNIQUEIDENTIFIER = '22220001-0000-7000-8000-000000000008';
DECLARE @Now DATETIMEOFFSET(7) = SYSDATETIMEOFFSET();

DECLARE @Products TABLE
(
    ProductId UNIQUEIDENTIFIER NOT NULL,
    BarcodeId UNIQUEIDENTIFIER NOT NULL,
    PriceId UNIQUEIDENTIFIER NOT NULL,
    Code NVARCHAR(32) NOT NULL,
    Reference NVARCHAR(80) NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Barcode NVARCHAR(64) NOT NULL,
    Price DECIMAL(19,4) NOT NULL
);

INSERT @Products(ProductId,BarcodeId,PriceId,Code,Reference,Name,Barcode,Price)
VALUES
('22220002-0000-7000-8000-000000000001','22220002-0000-7000-8000-000000000011','22220002-0000-7000-8000-000000000021',N'DEMO-002',N'REF-DEMO-002',N'Jabón líquido 500 ml',N'7700000000002',18500),
('22220002-0000-7000-8000-000000000002','22220002-0000-7000-8000-000000000012','22220002-0000-7000-8000-000000000022',N'DEMO-003',N'REF-DEMO-003',N'Shampoo familiar 400 ml',N'7700000000003',24900),
('22220002-0000-7000-8000-000000000003','22220002-0000-7000-8000-000000000013','22220002-0000-7000-8000-000000000023',N'DEMO-004',N'REF-DEMO-004',N'Crema corporal 250 ml',N'7700000000004',31700),
('22220002-0000-7000-8000-000000000004','22220002-0000-7000-8000-000000000014','22220002-0000-7000-8000-000000000024',N'DEMO-005',N'REF-DEMO-005',N'Pañitos húmedos x 100',N'7700000000005',15900),
('22220002-0000-7000-8000-000000000005','22220002-0000-7000-8000-000000000015','22220002-0000-7000-8000-000000000025',N'DEMO-006',N'REF-DEMO-006',N'Aceite corporal 120 ml',N'7700000000006',22800);

INSERT dbo.Products(
    ProductId,TenantId,BusinessId,ProductCode,Reference,BaseUnitCode,TaxProfileId,
    Source,Sku,Name,Currency,ManageStock,IsWeighable,IsActive,CreatedAt)
SELECT p.ProductId,(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId),@BusinessId,p.Code,p.Reference,N'EA',@TaxProfileId,
       0,p.Code,p.Name,N'COP',1,0,1,@Now
FROM @Products p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Products x WHERE x.ProductId=p.ProductId);

INSERT dbo.ProductBarcodes(
    ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
SELECT p.BarcodeId,@BusinessId,p.ProductId,p.Barcode,1,1,@Now
FROM @Products p
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductBarcodes x WHERE x.ProductBarcodeId=p.BarcodeId);

INSERT dbo.ProductPrices(
    ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
SELECT p.PriceId,@BusinessId,p.ProductId,p.Price,N'COP',DATEADD(day,-1,@Now),1,@Now
FROM @Products p
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductPrices x WHERE x.ProductPriceId=p.PriceId);

INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
SELECT @BusinessId,p.ProductId,N'Upsert',@Now
FROM @Products p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.CatalogChanges x
    WHERE x.BusinessId=@BusinessId AND x.ProductId=p.ProductId);

COMMIT TRANSACTION;

SELECT p.ProductCode,p.Name,b.Barcode,pp.Amount
FROM dbo.Products p
JOIN dbo.ProductBarcodes b ON b.ProductId=p.ProductId AND b.IsActive=1
JOIN dbo.ProductPrices pp ON pp.ProductId=p.ProductId AND pp.IsActive=1
WHERE p.BusinessId=@BusinessId
ORDER BY p.ProductCode;
