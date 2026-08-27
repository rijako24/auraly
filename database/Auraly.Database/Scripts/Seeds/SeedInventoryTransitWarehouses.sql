SET NOCOUNT ON;
INSERT dbo.Warehouses
  (WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,
   IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
SELECT NEWID(),b.BusinessId,N'TRA',N'Mercancía en tránsito',0,
       COALESCE((SELECT TOP(1) w.PriceFormationCostBasis FROM dbo.Warehouses w WHERE w.BusinessId=b.BusinessId ORDER BY w.CreatedAt),N'LatestReceiptCost'),
       1,0,0,0,1,SYSDATETIMEOFFSET()
FROM dbo.Businesses b
WHERE NOT EXISTS(SELECT 1 FROM dbo.Warehouses w WHERE w.BusinessId=b.BusinessId AND w.Code=N'TRA');

-- Migrate previously processed one-step transfers into the only retained system mode.
UPDATE dbo.InventoryOperations
SET TransferMode=N'ImmediateSystem'
WHERE DocumentType=N'WarehouseTransfer' AND TransferMode IS NULL;

UPDATE line
SET DispatchedQuantity=line.Quantity,
    ReceivedQuantity=line.Quantity,
    DispatchUnitCost=line.ProcessedUnitCost,
    DispatchValue=line.ProcessedValue
FROM dbo.InventoryOperationLines line
INNER JOIN dbo.InventoryOperations operation
  ON operation.InventoryOperationId=line.InventoryOperationId
WHERE operation.DocumentType=N'WarehouseTransfer'
  AND line.Direction=N'TRANSFER'
  AND line.DispatchedQuantity IS NULL;
