CREATE PROCEDURE [dbo].[SellerOrderEditableGet]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @WorkSessionId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT o.ExternalDocumentNumber,o.CustomerId,o.Status,
           COALESCE(o.WarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(CASE WHEN ISJSON(o.CustomAttributesJson)=1 THEN o.CustomAttributesJson END,'$.WarehouseId'))),
           COALESCE(o.OrdersWarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(CASE WHEN ISJSON(o.CustomAttributesJson)=1 THEN o.CustomAttributesJson END,'$.ordersWarehouseId')))
    FROM dbo.Orders o WITH(UPDLOCK,HOLDLOCK)
    WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId AND o.Source=1
      AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link WHERE link.OrderId=o.OrderId);

    SELECT item.ProductId,
           SUM(item.Quantity),
           SUM(CASE WHEN orders.ExternalStatus=N'InventoryReleasedForInvoice' THEN 0 ELSE COALESCE(
             TRY_CONVERT(DECIMAL(19,6),JSON_VALUE(
               CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
               '$.ReservedQuantity')),
             CASE WHEN orders.Status=2 THEN item.Quantity ELSE 0 END) END),
           MAX(item.UnitPrice),SUM(item.DiscountAmount),
           COALESCE(NULLIF(MAX(JSON_VALUE(
             CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
             '$.PriceSource')),N''),N'Captured'),
           CAST(MAX(CAST(COALESCE(product.ManageStock,0) AS INT)) AS BIT)
    FROM dbo.OrderItems item
    INNER JOIN dbo.Orders orders
      ON orders.OrderId=item.OrderId AND orders.BusinessId=item.BusinessId
    LEFT JOIN dbo.Products product
      ON product.ProductId=item.ProductId AND product.BusinessId=item.BusinessId
    WHERE item.OrderId=@OrderId AND item.ProductId IS NOT NULL
    GROUP BY item.ProductId;
END
