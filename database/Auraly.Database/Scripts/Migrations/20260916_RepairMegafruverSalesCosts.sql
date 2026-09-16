SET XACT_ABORT ON;

IF EXISTS
(
    SELECT 1 FROM sys.extended_properties
    WHERE class=0 AND name=N'Auraly.DataRepair.20260916.MegafruverSalesCosts'
)
    RETURN;

BEGIN TRANSACTION;

DECLARE @BusinessId UNIQUEIDENTIFIER=(
    SELECT TOP(1) business.BusinessId
    FROM dbo.Businesses business
    JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId
    WHERE tenant.Name=N'MEGAFRUVER'
    ORDER BY business.CreatedAt);

IF @BusinessId IS NULL
BEGIN
    COMMIT TRANSACTION;
    RETURN;
END;

CREATE TABLE #CostRepair
(
    DocumentId UNIQUEIDENTIFIER NOT NULL,
    LineNumber INT NOT NULL,
    FactId UNIQUEIDENTIFIER NOT NULL,
    BusinessLocalDate DATE NOT NULL,
    OldCost DECIMAL(19,4) NOT NULL,
    NewCost DECIMAL(19,4) NOT NULL,
    PRIMARY KEY(DocumentId,LineNumber)
);

INSERT #CostRepair(DocumentId,LineNumber,FactId,BusinessLocalDate,OldCost,NewCost)
SELECT document.DocumentId,line.LineNumber,fact.FactId,fact.BusinessLocalDate,
       fact.RecognizedCostAmount,
       ROUND(line.Quantity*price.CostBasisAmount,4)
FROM dbo.SalesDocuments document
JOIN dbo.SalesDocumentLines line ON line.DocumentId=document.DocumentId
JOIN reporting.SalesReportLineFacts fact
  ON fact.SourceDocumentId=document.DocumentId
 AND fact.SourceDocumentType=document.DocumentType
 AND fact.SourceLineNumber=line.LineNumber
 AND fact.MovementType=N'Sale'
CROSS APPLY
(
    SELECT TOP(1) value.CostBasisAmount
    FROM dbo.ProductPrices value
    WHERE value.BusinessId=document.BusinessId AND value.ProductId=line.ProductId
      AND value.IsActive=1 AND value.CostBasisAmount IS NOT NULL
    ORDER BY value.ValidFrom DESC,value.ProductPriceId
) price
WHERE document.BusinessId=@BusinessId
  AND CONVERT(date,SWITCHOFFSET(document.IssuedAt,'-05:00'))
      BETWEEN CONVERT(date,'2026-09-14') AND CONVERT(date,'2026-09-15')
  AND document.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
  AND fact.RecognizedCostAmount<>ROUND(line.Quantity*price.CostBasisAmount,4);

IF EXISTS
(
    SELECT 1
    FROM dbo.SalesDocuments document
    JOIN dbo.SalesDocumentLines line ON line.DocumentId=document.DocumentId
    WHERE document.BusinessId=@BusinessId
      AND CONVERT(date,SWITCHOFFSET(document.IssuedAt,'-05:00'))
          BETWEEN CONVERT(date,'2026-09-14') AND CONVERT(date,'2026-09-15')
      AND document.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.ProductPrices price
          WHERE price.BusinessId=document.BusinessId AND price.ProductId=line.ProductId
            AND price.IsActive=1 AND price.CostBasisAmount IS NOT NULL
      )
)
    THROW 51413,N'Hay líneas de Megafruver sin costo vigente; la reparación fue cancelada.',1;

UPDATE line
SET UnitCostSnapshot=price.CostBasisAmount
FROM dbo.SalesDocumentLines line
JOIN dbo.SalesDocuments document ON document.DocumentId=line.DocumentId
CROSS APPLY
(
    SELECT TOP(1) value.CostBasisAmount
    FROM dbo.ProductPrices value
    WHERE value.BusinessId=document.BusinessId AND value.ProductId=line.ProductId
      AND value.IsActive=1 AND value.CostBasisAmount IS NOT NULL
    ORDER BY value.ValidFrom DESC,value.ProductPriceId
) price
WHERE document.BusinessId=@BusinessId
  AND CONVERT(date,SWITCHOFFSET(document.IssuedAt,'-05:00'))
      BETWEEN CONVERT(date,'2026-09-14') AND CONVERT(date,'2026-09-15')
  AND document.DocumentType IN(N'SalesInvoice',N'SalesReceipt');

UPDATE fact SET RecognizedCostAmount=repair.NewCost
FROM reporting.SalesReportLineFacts fact
JOIN #CostRepair repair ON repair.FactId=fact.FactId;

UPDATE documentFact
SET RecognizedCostAmount=costValue.TotalCost
FROM reporting.SalesReportDocuments documentFact
CROSS APPLY
(
    SELECT SUM(lineFact.RecognizedCostAmount) TotalCost
    FROM reporting.SalesReportLineFacts lineFact
    WHERE lineFact.OriginalSaleDocumentId=documentFact.DocumentId
      AND lineFact.MovementType=N'Sale'
) costValue
WHERE documentFact.BusinessId=@BusinessId
  AND documentFact.BusinessLocalDate BETWEEN CONVERT(date,'2026-09-14') AND CONVERT(date,'2026-09-15');

;WITH delta AS
(
    SELECT BusinessLocalDate,SUM(NewCost-OldCost) CostDelta
    FROM #CostRepair GROUP BY BusinessLocalDate
)
UPDATE total
SET NetRecognizedCost=total.NetRecognizedCost+delta.CostDelta,
    GrossProfit=total.GrossProfit-delta.CostDelta,
    UpdatedAt=SYSDATETIMEOFFSET()
FROM reporting.SalesReportDailyTotals total
JOIN delta ON delta.BusinessLocalDate=total.BusinessLocalDate
WHERE total.BusinessId=@BusinessId AND total.CurrencyCode=N'COP';

;WITH dimensionDelta AS
(
    SELECT repair.BusinessLocalDate,value.DimensionType,value.DimensionKey,
           SUM(repair.NewCost-repair.OldCost) CostDelta
    FROM #CostRepair repair
    JOIN reporting.SalesReportLineFacts fact ON fact.FactId=repair.FactId
    JOIN reporting.SalesReportDocuments documentFact
      ON documentFact.DocumentId=fact.OriginalSaleDocumentId
    CROSS APPLY(VALUES
      (N'Customer',COALESCE(CONVERT(nvarchar(80),documentFact.CustomerId),N'final-consumer')),
      (N'Seller',COALESCE(CONVERT(nvarchar(80),documentFact.SellerId),N'no-seller')),
      (N'Supplier',COALESCE(CONVERT(nvarchar(80),fact.SupplierId),N'no-supplier')),
      (N'Product',CONVERT(nvarchar(80),fact.ProductId)),
      (N'Category',COALESCE(CONVERT(nvarchar(80),fact.CategoryId),N'no-category')),
      (N'Warehouse',CONVERT(nvarchar(80),documentFact.WarehouseId))
    ) value(DimensionType,DimensionKey)
    GROUP BY repair.BusinessLocalDate,value.DimensionType,value.DimensionKey
)
UPDATE total
SET NetRecognizedCost=total.NetRecognizedCost+delta.CostDelta,
    GrossProfit=total.GrossProfit-delta.CostDelta,
    UpdatedAt=SYSDATETIMEOFFSET()
FROM reporting.SalesReportDailyDimensionTotals total
JOIN dimensionDelta delta ON delta.BusinessLocalDate=total.BusinessLocalDate
 AND delta.DimensionType=total.DimensionType AND delta.DimensionKey=total.DimensionKey
WHERE total.BusinessId=@BusinessId AND total.CurrencyCode=N'COP';

EXEC sys.sp_addextendedproperty
    @name=N'Auraly.DataRepair.20260916.MegafruverSalesCosts',
    @value=N'Applied';

COMMIT TRANSACTION;
