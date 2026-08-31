CREATE PROCEDURE [dbo].[CatalogProductRotationGet]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.Products product JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
        WHERE product.ProductId=@ProductId AND business.TenantId=@TenantId
          AND (product.TenantId=@TenantId OR product.BusinessId=@BusinessId)
    ) THROW 51010,'Product not found.',1;

    SELECT warehouse.WarehouseId,warehouse.Code,warehouse.Name,rotation.GrossUnitsSold30Days,
      rotation.ReturnedUnits30Days,rotation.NetUnitsSold30Days,rotation.GrossUnitsSold90Days,
      rotation.ReturnedUnits90Days,rotation.NetUnitsSold90Days,rotation.DailyDemand90Days,
      COALESCE(balance.QuantityOnHand,0),COALESCE(incoming.Quantity,0),
      CASE WHEN rotation.DailyDemand90Days>0 THEN COALESCE(balance.QuantityOnHand,0)/rotation.DailyDemand90Days END,
      rotation.WindowEndDate,rotation.CalculatedAt
    FROM reporting.ProductRotationSnapshots rotation
    JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=rotation.WarehouseId
      AND warehouse.BusinessId=rotation.BusinessId AND warehouse.IsActive=1
    LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=rotation.BusinessId
      AND balance.WarehouseId=rotation.WarehouseId AND balance.ProductId=rotation.ProductId
    OUTER APPLY
    (
      SELECT SUM(CASE WHEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity>0
        THEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity ELSE 0 END) Quantity
      FROM purchasing.PurchaseOrderLines line
      JOIN purchasing.PurchaseOrders purchaseOrder ON purchaseOrder.PurchaseOrderId=line.PurchaseOrderId
      WHERE purchaseOrder.BusinessId=rotation.BusinessId AND purchaseOrder.WarehouseId=rotation.WarehouseId
        AND purchaseOrder.Status IN(N'Open',N'PartiallyReceived') AND line.ProductId=rotation.ProductId
    ) incoming
    WHERE rotation.BusinessId=@BusinessId AND rotation.ProductId=@ProductId
    ORDER BY warehouse.Name,warehouse.Code;
END;
