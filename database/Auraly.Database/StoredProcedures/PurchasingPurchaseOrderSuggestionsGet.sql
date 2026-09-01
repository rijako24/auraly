CREATE PROCEDURE [purchasing].[PurchaseOrderSuggestionsGet]
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @SupplierId UNIQUEIDENTIFIER,
    @ProductIdsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND IsActive=1)
       OR NOT EXISTS(SELECT 1 FROM dbo.Suppliers WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND IsActive=1)
        THROW 51220,'The warehouse or supplier is outside the authenticated business.',1;

    DECLARE @ProductIds TABLE(ProductId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
    INSERT @ProductIds(ProductId)
    SELECT DISTINCT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@ProductIdsJson)
    WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL;

    SELECT p.ProductId,COALESCE(r.NetUnitsSold30Days,0),COALESCE(r.NetUnitsSold90Days,0),
        COALESCE(r.DailyDemand90Days,0),COALESCE(b.QuantityOnHand,0),COALESCE(incoming.Quantity,0),
        COALESCE(sp.PurchasePresentationName,N'Unidad'),COALESCE(NULLIF(sp.UnitsPerPresentation,0),1),r.CalculatedAt
    FROM @ProductIds ids
    JOIN dbo.Products p ON p.ProductId=ids.ProductId
    JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
      AND (p.TenantId=business.TenantId OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId))
    OUTER APPLY
    (
      SELECT TOP(1) supplierProduct.PurchasePresentationName,supplierProduct.UnitsPerPresentation
      FROM dbo.SupplierProducts supplierProduct
      WHERE supplierProduct.BusinessId=@BusinessId AND supplierProduct.SupplierId=@SupplierId
        AND supplierProduct.ProductId=p.ProductId AND supplierProduct.IsActive=1
      ORDER BY supplierProduct.IsPrimary DESC,supplierProduct.SupplierProductId
    ) sp
    LEFT JOIN reporting.ProductRotationSnapshots r ON r.BusinessId=@BusinessId
      AND r.WarehouseId=@WarehouseId AND r.ProductId=p.ProductId
    LEFT JOIN dbo.InventoryBalances b ON b.BusinessId=@BusinessId
      AND b.WarehouseId=@WarehouseId AND b.ProductId=p.ProductId
    OUTER APPLY
    (
      SELECT SUM(CASE WHEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity>0
        THEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity ELSE 0 END) Quantity
      FROM purchasing.PurchaseOrderLines line
      JOIN purchasing.PurchaseOrders purchaseOrder ON purchaseOrder.PurchaseOrderId=line.PurchaseOrderId
      WHERE purchaseOrder.BusinessId=@BusinessId AND purchaseOrder.WarehouseId=@WarehouseId
        AND purchaseOrder.Status IN(N'Open',N'PartiallyReceived') AND line.ProductId=p.ProductId
    ) incoming
    WHERE p.IsActive=1
    ORDER BY p.ProductId;
END;
