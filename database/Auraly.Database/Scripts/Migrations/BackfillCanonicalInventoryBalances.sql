SET NOCOUNT ON;

-- InventoryBalances is the canonical current-stock projection. Every product and
-- every warehouse, including system warehouses, owns exactly one balance row.
INSERT dbo.InventoryBalances
  (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
   InventoryValue,LastProcessingSequence,UpdatedAt)
SELECT product.BusinessId,warehouse.WarehouseId,product.ProductId,0,0,0,
       COALESCE(processingCursor.LastCompletedSequence,0),SYSDATETIMEOFFSET()
FROM dbo.Products product
INNER JOIN dbo.Warehouses warehouse ON warehouse.BusinessId=product.BusinessId
LEFT JOIN dbo.BusinessProcessingCursors processingCursor ON processingCursor.BusinessId=product.BusinessId
WHERE NOT EXISTS (
  SELECT 1
  FROM dbo.InventoryBalances balance
  WHERE balance.BusinessId=product.BusinessId
    AND balance.WarehouseId=warehouse.WarehouseId
    AND balance.ProductId=product.ProductId);
