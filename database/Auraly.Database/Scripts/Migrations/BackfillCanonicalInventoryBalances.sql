SET NOCOUNT ON;

-- InventoryBalances is the canonical current-stock projection. Every product and
-- every warehouse, including system warehouses, owns exactly one balance row.
INSERT dbo.InventoryBalances
  (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
   InventoryValue,LastProcessingSequence,UpdatedAt)
SELECT warehouse.BusinessId,warehouse.WarehouseId,product.ProductId,0,0,0,
       COALESCE(processingCursor.LastCompletedSequence,0),SYSDATETIMEOFFSET()
FROM dbo.Warehouses warehouse
JOIN dbo.Businesses business ON business.BusinessId=warehouse.BusinessId
JOIN dbo.Products product ON product.TenantId=business.TenantId
LEFT JOIN dbo.BusinessProcessingCursors processingCursor ON processingCursor.BusinessId=warehouse.BusinessId
WHERE NOT EXISTS (
  SELECT 1
  FROM dbo.InventoryBalances balance
  WHERE balance.BusinessId=warehouse.BusinessId
    AND balance.WarehouseId=warehouse.WarehouseId
    AND balance.ProductId=product.ProductId);
