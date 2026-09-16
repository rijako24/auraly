SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.OrderItems',N'DocumentUnitCost') IS NOT NULL
   AND COL_LENGTH(N'dbo.OrderDraftItems',N'DocumentUnitCost') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDraftLines',N'PublicUnitPrice') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDraftLines',N'PublicLineTotal') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDraftLines',N'PublicDiscountAmount') IS NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.columns
       WHERE (object_id=OBJECT_ID(N'dbo.OrderItems') AND name=N'DocumentUnitCost' AND is_nullable=1)
          OR (object_id=OBJECT_ID(N'dbo.OrderDraftItems') AND name=N'DocumentUnitCost' AND is_nullable=1)
          OR (object_id=OBJECT_ID(N'dbo.SalesDraftLines') AND name IN(N'PublicUnitPrice',N'PublicLineTotal') AND is_nullable=1)
   )
   AND NOT EXISTS
   (
       SELECT 1 FROM dbo.SalesDraftLines
       WHERE ABS(ROUND(PublicUnitPrice*Quantity-DiscountAmount-PromotionDiscountAmount,2)-PublicLineTotal)>0.001
   )
    RETURN;

BEGIN TRANSACTION;

;WITH itemCosts AS
(
    SELECT item.OrderItemId,COALESCE(price.CostBasisAmount,0) DocumentUnitCost
    FROM dbo.OrderItems item
    OUTER APPLY
    (
        SELECT TOP(1) value.CostBasisAmount
        FROM dbo.ProductPrices value
        WHERE value.BusinessId=item.BusinessId AND value.ProductId=item.ProductId
          AND value.IsActive=1
        ORDER BY value.ValidFrom DESC,value.ProductPriceId
    ) price
)
UPDATE item SET DocumentUnitCost=source.DocumentUnitCost
FROM dbo.OrderItems item
JOIN itemCosts source ON source.OrderItemId=item.OrderItemId
WHERE item.DocumentUnitCost IS NULL;

;WITH itemCosts AS
(
    SELECT item.OrderDraftItemId,COALESCE(price.CostBasisAmount,0) DocumentUnitCost
    FROM dbo.OrderDraftItems item
    OUTER APPLY
    (
        SELECT TOP(1) value.CostBasisAmount
        FROM dbo.ProductPrices value
        WHERE value.BusinessId=item.BusinessId AND value.ProductId=item.ProductId
          AND value.IsActive=1
        ORDER BY value.ValidFrom DESC,value.ProductPriceId
    ) price
)
UPDATE item SET DocumentUnitCost=source.DocumentUnitCost
FROM dbo.OrderDraftItems item
JOIN itemCosts source ON source.OrderDraftItemId=item.OrderDraftItemId
WHERE item.DocumentUnitCost IS NULL;

IF EXISTS(SELECT 1 FROM dbo.OrderItems WHERE DocumentUnitCost IS NULL)
    THROW 51410,N'No se pudo completar el costo congelado de todas las líneas de pedido.',1;
IF EXISTS(SELECT 1 FROM dbo.OrderDraftItems WHERE DocumentUnitCost IS NULL)
    THROW 51411,N'No se pudo completar el costo congelado de todos los borradores de pedido.',1;

IF EXISTS(SELECT 1 FROM sys.columns
          WHERE object_id=OBJECT_ID(N'dbo.OrderItems')
            AND name=N'DocumentUnitCost' AND is_nullable=1)
    ALTER TABLE dbo.OrderItems ALTER COLUMN DocumentUnitCost DECIMAL(19,6) NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns
          WHERE object_id=OBJECT_ID(N'dbo.OrderDraftItems')
            AND name=N'DocumentUnitCost' AND is_nullable=1)
    ALTER TABLE dbo.OrderDraftItems ALTER COLUMN DocumentUnitCost DECIMAL(19,6) NOT NULL;

CREATE TABLE #LegacySalesDraftLines(SalesDraftLineId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
INSERT #LegacySalesDraftLines(SalesDraftLineId)
SELECT SalesDraftLineId
FROM dbo.SalesDraftLines
WHERE PublicUnitPrice IS NULL OR PublicLineTotal IS NULL;

UPDATE line
SET PublicUnitPrice=CEILING(line.UnitPrice*(1+line.TaxRate/100)*100)/100,
    DiscountAmount=ROUND(line.DiscountAmount*(1+line.TaxRate/100),2),
    PromotionDiscountAmount=ROUND(line.PromotionDiscountAmount*(1+line.TaxRate/100),2),
    DocumentUnitCost=CASE WHEN line.DocumentUnitCost>0 THEN line.DocumentUnitCost
                          ELSE COALESCE(price.CostBasisAmount,0) END
FROM dbo.SalesDraftLines line
JOIN #LegacySalesDraftLines legacy ON legacy.SalesDraftLineId=line.SalesDraftLineId
JOIN dbo.SalesDrafts draft ON draft.SalesDraftId=line.SalesDraftId
OUTER APPLY
(
    SELECT TOP(1) value.CostBasisAmount
    FROM dbo.ProductPrices value
    WHERE value.BusinessId=draft.BusinessId AND value.ProductId=line.ProductId
      AND value.IsActive=1
    ORDER BY value.ValidFrom DESC,value.ProductPriceId
) price;

IF COL_LENGTH(N'dbo.SalesDraftLines',N'PublicDiscountAmount') IS NOT NULL
BEGIN
    EXEC(N'
      UPDATE line
      SET DiscountAmount=CASE
        WHEN line.PublicDiscountAmount>line.PromotionDiscountAmount
          THEN line.PublicDiscountAmount-line.PromotionDiscountAmount
        ELSE 0 END
      FROM dbo.SalesDraftLines line
      WHERE line.PublicDiscountAmount IS NOT NULL;');
END;

UPDATE line
SET PublicUnitPrice=item.UnitPrice,
    DiscountAmount=CASE WHEN JSON_VALUE(
      CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
      '$.PriceSource')=N'Promotion' THEN 0 ELSE item.DiscountAmount END,
    PromotionDiscountAmount=CASE WHEN JSON_VALUE(
      CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
      '$.PriceSource')=N'Promotion' THEN item.DiscountAmount ELSE 0 END,
    PublicLineTotal=item.LineTotal,
    DocumentUnitCost=item.DocumentUnitCost
FROM dbo.SalesDraftLines line
JOIN dbo.SalesDrafts draft ON draft.SalesDraftId=line.SalesDraftId
JOIN dbo.OrderItems item ON item.OrderId=draft.SourceOrderId
 AND item.ProductId=line.ProductId
 AND TRY_CONVERT(int,JSON_VALUE(item.RawPayloadJson,'$.LinePosition'))=line.Position
WHERE draft.SourceOrderId IS NOT NULL;

UPDATE line
SET DocumentUnitCost=price.CostBasisAmount
FROM dbo.SalesDraftLines line
JOIN dbo.SalesDrafts draft ON draft.SalesDraftId=line.SalesDraftId
CROSS APPLY
(
    SELECT TOP(1) value.CostBasisAmount
    FROM dbo.ProductPrices value
    WHERE value.BusinessId=draft.BusinessId AND value.ProductId=line.ProductId
      AND value.IsActive=1 AND value.CostBasisAmount IS NOT NULL
    ORDER BY value.ValidFrom DESC,value.ProductPriceId
) price
WHERE line.DocumentUnitCost=0 AND price.CostBasisAmount>0;

UPDATE line
SET PublicLineTotal=ROUND(
      line.PublicUnitPrice*line.Quantity-line.DiscountAmount-line.PromotionDiscountAmount,2)
FROM dbo.SalesDraftLines line
WHERE line.PublicLineTotal IS NULL;

UPDATE line
SET DiscountAmount=ROUND(
      line.PublicUnitPrice*line.Quantity-line.PromotionDiscountAmount-line.PublicLineTotal,2)
FROM dbo.SalesDraftLines line
WHERE ABS(ROUND(
        line.PublicUnitPrice*line.Quantity-line.DiscountAmount-line.PromotionDiscountAmount,2)
      -line.PublicLineTotal)>0.001
  AND line.PublicUnitPrice*line.Quantity-line.PromotionDiscountAmount-line.PublicLineTotal>=0;

IF EXISTS(SELECT 1 FROM dbo.SalesDraftLines
          WHERE PublicUnitPrice IS NULL OR PublicLineTotal IS NULL)
    THROW 51412,N'No se pudieron completar los valores públicos de todos los drafts web.',1;

IF EXISTS(SELECT 1 FROM sys.check_constraints
          WHERE parent_object_id=OBJECT_ID(N'dbo.SalesDraftLines')
            AND name=N'CK_SalesDraftLines_Amounts')
    ALTER TABLE dbo.SalesDraftLines DROP CONSTRAINT CK_SalesDraftLines_Amounts;

IF COL_LENGTH(N'dbo.SalesDraftLines',N'PublicDiscountAmount') IS NOT NULL
    EXEC(N'ALTER TABLE dbo.SalesDraftLines DROP COLUMN PublicDiscountAmount;');

IF EXISTS(SELECT 1 FROM sys.columns
          WHERE object_id=OBJECT_ID(N'dbo.SalesDraftLines')
            AND name=N'PublicUnitPrice' AND is_nullable=1)
    ALTER TABLE dbo.SalesDraftLines ALTER COLUMN PublicUnitPrice DECIMAL(18,2) NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns
          WHERE object_id=OBJECT_ID(N'dbo.SalesDraftLines')
            AND name=N'PublicLineTotal' AND is_nullable=1)
    ALTER TABLE dbo.SalesDraftLines ALTER COLUMN PublicLineTotal DECIMAL(18,2) NOT NULL;

DECLARE @MegafruverBusinessId UNIQUEIDENTIFIER=(
    SELECT TOP(1) business.BusinessId
    FROM dbo.Businesses business
    JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId
    WHERE tenant.Name=N'MEGAFRUVER'
    ORDER BY business.CreatedAt);

IF @MegafruverBusinessId IS NOT NULL
BEGIN
    UPDATE item
    SET UnitPrice=CEILING(item.UnitPrice*100)/100,
        LineTotal=ROUND(item.Quantity*(CEILING(item.UnitPrice*100)/100)-item.DiscountAmount,2),
        DocumentUnitCost=CASE WHEN item.DocumentUnitCost>0 THEN item.DocumentUnitCost
                              ELSE COALESCE(price.CostBasisAmount,item.DocumentUnitCost) END
    FROM dbo.OrderItems item
    JOIN dbo.Orders orderValue ON orderValue.OrderId=item.OrderId
    OUTER APPLY
    (
        SELECT TOP(1) value.CostBasisAmount
        FROM dbo.ProductPrices value
        WHERE value.BusinessId=item.BusinessId AND value.ProductId=item.ProductId
          AND value.IsActive=1
        ORDER BY value.ValidFrom DESC,value.ProductPriceId
    ) price
    WHERE orderValue.BusinessId=@MegafruverBusinessId
      AND orderValue.ExternalDocumentNumber LIKE N'PED-%'
      AND orderValue.Status IN(2,5)
      AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link
                     WHERE link.OrderId=orderValue.OrderId)
      AND (item.UnitPrice<>CEILING(item.UnitPrice*100)/100
           OR item.LineTotal<>ROUND(item.Quantity*(CEILING(item.UnitPrice*100)/100)-item.DiscountAmount,2)
           OR (item.DocumentUnitCost<=0 AND price.CostBasisAmount>0));

    UPDATE orderValue
    SET Subtotal=totals.Total,Total=totals.Total,UpdatedAt=SYSUTCDATETIME()
    FROM dbo.Orders orderValue
    CROSS APPLY(SELECT SUM(item.LineTotal) Total
                FROM dbo.OrderItems item WHERE item.OrderId=orderValue.OrderId) totals
    WHERE orderValue.BusinessId=@MegafruverBusinessId
      AND orderValue.ExternalDocumentNumber LIKE N'PED-%'
      AND orderValue.Status IN(2,5)
      AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link
                     WHERE link.OrderId=orderValue.OrderId)
      AND (orderValue.Subtotal<>totals.Total OR orderValue.Total<>totals.Total);
END;

COMMIT TRANSACTION;
