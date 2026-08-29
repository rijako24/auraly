-- Canonicaliza el producto a nivel tenant y materializa su estado operativo por sede.
IF EXISTS (
    SELECT 1
    FROM dbo.Products product
    INNER JOIN dbo.Businesses business ON business.BusinessId=product.BusinessId
    WHERE product.ProductCode IS NOT NULL
    GROUP BY business.TenantId,product.ProductCode
    HAVING COUNT(*)>1)
  THROW 51090, 'Existen codigos de producto duplicados entre sedes del mismo tenant. Deben consolidarse antes de activar el catalogo tenant-scoped.', 1;

UPDATE product
SET TenantId=business.TenantId
FROM dbo.Products product
INNER JOIN dbo.Businesses business ON business.BusinessId=product.BusinessId
WHERE product.TenantId IS NULL;

IF EXISTS (
    SELECT 1 FROM dbo.ProductBarcodes barcode
    INNER JOIN dbo.Businesses business ON business.BusinessId=barcode.BusinessId
    GROUP BY business.TenantId,barcode.Barcode
    HAVING COUNT(DISTINCT barcode.ProductId)>1)
  THROW 51091, 'Existen codigos de barras asignados a productos distintos dentro del mismo tenant.', 1;

INSERT dbo.ProductBarcodes(ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
SELECT NEWID(),business.BusinessId,source.ProductId,source.Barcode,source.IsPrimary,source.IsActive,SYSUTCDATETIME()
FROM (
  SELECT barcode.ProductId,barcode.Barcode,MAX(CONVERT(INT,barcode.IsPrimary)) IsPrimary,
         MAX(CONVERT(INT,barcode.IsActive)) IsActive
  FROM dbo.ProductBarcodes barcode GROUP BY barcode.ProductId,barcode.Barcode
) source
INNER JOIN dbo.Products product ON product.ProductId=source.ProductId
INNER JOIN dbo.Businesses business ON business.TenantId=product.TenantId AND business.IsActive=1
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductBarcodes existing
                  WHERE existing.BusinessId=business.BusinessId AND existing.Barcode=source.Barcode);

INSERT dbo.ProductIdentifiers(ProductIdentifierId,BusinessId,ProductId,IdentifierType,Value,IsActive,CreatedAt)
SELECT NEWID(),business.BusinessId,source.ProductId,source.IdentifierType,source.Value,source.IsActive,SYSUTCDATETIME()
FROM (
  SELECT identifier.ProductId,identifier.IdentifierType,identifier.Value,
         MAX(CONVERT(INT,identifier.IsActive)) IsActive
  FROM dbo.ProductIdentifiers identifier GROUP BY identifier.ProductId,identifier.IdentifierType,identifier.Value
) source
INNER JOIN dbo.Products product ON product.ProductId=source.ProductId
INNER JOIN dbo.Businesses business ON business.TenantId=product.TenantId AND business.IsActive=1
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductIdentifiers existing
                  WHERE existing.BusinessId=business.BusinessId
                    AND existing.IdentifierType=source.IdentifierType AND existing.Value=source.Value);

INSERT dbo.ProductPrices
  (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,CostBasisType,
   CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
   RoundingMode,PublishedByUserId,PublishedAt,ValidFrom,ValidUntil,IsActive,CreatedAt)
SELECT NEWID(),business.BusinessId,product.ProductId,
       COALESCE(sourcePrice.Amount,0),COALESCE(sourcePrice.PreparedAmount,sourcePrice.Amount,0),
       COALESCE(sourcePrice.CurrencyCode,product.Currency,N'COP'),sourcePrice.CostBasisType,
       sourcePrice.CostBasisAmount,sourcePrice.TargetMarginPercent,sourcePrice.EffectiveMarginPercent,
       sourcePrice.InputMode,sourcePrice.RoundingIncrement,sourcePrice.RoundingMode,
       sourcePrice.PublishedByUserId,sourcePrice.PublishedAt,
       COALESCE(sourcePrice.ValidFrom,SYSUTCDATETIME()),NULL,1,SYSUTCDATETIME()
FROM dbo.Products product
INNER JOIN dbo.Businesses business ON business.TenantId=product.TenantId AND business.IsActive=1
OUTER APPLY (
  SELECT TOP(1) price.*
  FROM dbo.ProductPrices price
  WHERE price.ProductId=product.ProductId AND price.IsActive=1
  ORDER BY CASE WHEN price.BusinessId=product.BusinessId THEN 0 ELSE 1 END,price.ValidFrom DESC
) sourcePrice
WHERE NOT EXISTS (
  SELECT 1 FROM dbo.ProductPrices existing
  WHERE existing.BusinessId=business.BusinessId AND existing.ProductId=product.ProductId AND existing.IsActive=1);

INSERT dbo.InventoryBalances
  (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,
   LastProcessingSequence,UpdatedAt)
SELECT warehouse.BusinessId,warehouse.WarehouseId,product.ProductId,0,
       COALESCE(sourceBalance.AverageUnitCost,0),0,0,SYSUTCDATETIME()
FROM dbo.Products product
INNER JOIN dbo.Businesses business ON business.TenantId=product.TenantId AND business.IsActive=1
INNER JOIN dbo.Warehouses warehouse ON warehouse.BusinessId=business.BusinessId AND warehouse.IsActive=1
OUTER APPLY (
  SELECT TOP(1) balance.AverageUnitCost
  FROM dbo.InventoryBalances balance
  WHERE balance.ProductId=product.ProductId
  ORDER BY balance.UpdatedAt DESC
) sourceBalance
WHERE product.ManageStock=1 AND NOT EXISTS (
  SELECT 1 FROM dbo.InventoryBalances existing
  WHERE existing.BusinessId=warehouse.BusinessId AND existing.WarehouseId=warehouse.WarehouseId
    AND existing.ProductId=product.ProductId);
