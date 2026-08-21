UPDATE dbo.Warehouses
SET IsSystem=1,UseForSales=0,UseForGoodsReceipts=0,IsInventoryVisible=0
WHERE Code IN(N'PED',N'AVE')
  AND (IsSystem<>1 OR UseForSales<>0 OR UseForGoodsReceipts<>0 OR IsInventoryVisible<>0);

INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
SELECT NEWID(),b.BusinessId,N'AVE',N'Bodega de averías',0,N'LatestReceiptCost',1,0,0,0,1,SYSUTCDATETIME()
FROM dbo.Businesses b
WHERE b.IsActive=1 AND NOT EXISTS(
  SELECT 1 FROM dbo.Warehouses w WHERE w.BusinessId=b.BusinessId AND w.Code=N'AVE');

UPDATE dbo.Warehouses
SET UseForGoodsReceipts=UseForSales,IsInventoryVisible=UseForSales
WHERE IsSystem=0 AND (UseForGoodsReceipts<>UseForSales OR IsInventoryVisible<>UseForSales);
GO
