-- Historic restaurant demand is needed immediately after the new projection is deployed.
-- Only missing dates in the current forecasting window are inserted; retries are safe.
INSERT reporting.ProductDailyRotationSnapshots
  (BusinessId,WarehouseId,ProductId,BusinessLocalDate,NetQuantity,
   ProjectionVersion,CalculatedAt)
SELECT fact.BusinessId,fact.WarehouseId,fact.ProductId,
       fact.BusinessLocalDate,SUM(fact.Quantity),3,SYSDATETIMEOFFSET()
FROM reporting.SalesReportLineFacts fact
JOIN dbo.Businesses business ON business.BusinessId=fact.BusinessId
  AND business.PointOfSaleMode=N'RestaurantBar'
WHERE fact.BusinessLocalDate>=DATEADD(day,-56,CONVERT(date,SYSUTCDATETIME()))
  AND fact.WarehouseId IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1 FROM reporting.ProductDailyRotationSnapshots existing WITH(UPDLOCK,HOLDLOCK)
      WHERE existing.BusinessId=fact.BusinessId
        AND existing.WarehouseId=fact.WarehouseId
        AND existing.ProductId=fact.ProductId
        AND existing.BusinessLocalDate=fact.BusinessLocalDate
  )
GROUP BY fact.BusinessId,fact.WarehouseId,fact.ProductId,
         fact.BusinessLocalDate;
