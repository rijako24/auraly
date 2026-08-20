UPDATE dbo.Warehouses
SET IsSystem=1,UseForSales=0,UseForGoodsReceipts=0,IsInventoryVisible=0
WHERE Code=N'PED'
  AND (IsSystem<>1 OR UseForSales<>0 OR UseForGoodsReceipts<>0 OR IsInventoryVisible<>0);
GO
