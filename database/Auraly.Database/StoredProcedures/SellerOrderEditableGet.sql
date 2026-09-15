CREATE PROCEDURE [dbo].[SellerOrderEditableGet]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @WorkSessionId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT o.ExternalDocumentNumber,o.CustomerId,o.PartySiteId,o.Status,
           COALESCE(o.WarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(CASE WHEN ISJSON(o.CustomAttributesJson)=1 THEN o.CustomAttributesJson END,'$.WarehouseId'))),
           COALESCE(o.OrdersWarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(CASE WHEN ISJSON(o.CustomAttributesJson)=1 THEN o.CustomAttributesJson END,'$.ordersWarehouseId')))
    FROM dbo.Orders o WITH(UPDLOCK,HOLDLOCK)
    WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId AND o.Source=1
      AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link WHERE link.OrderId=o.OrderId);

    SELECT COALESCE(TRY_CONVERT(INT,JSON_VALUE(
             CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
             '$.LinePosition')),
             CAST(ROW_NUMBER() OVER(ORDER BY item.CreatedAt,item.OrderItemId) AS INT)),
           item.ProductId,
           item.Quantity,
           CASE WHEN orders.ExternalStatus=N'InventoryReleasedForInvoice' THEN 0 ELSE COALESCE(
             TRY_CONVERT(DECIMAL(19,6),JSON_VALUE(
               CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
               '$.ReservedQuantity')),
             CASE WHEN orders.Status=2 THEN item.Quantity ELSE 0 END) END,
           item.UnitPrice,item.DiscountAmount,
           COALESCE(NULLIF(JSON_VALUE(
             CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
             '$.PriceSource'),N''),N'Captured'),
           TRY_CONVERT(DECIMAL(19,6),JSON_VALUE(
             CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
             '$.DocumentUnitCost')),
           CAST(COALESCE(product.ManageStock,0) AS BIT)
    FROM dbo.OrderItems item
    INNER JOIN dbo.Orders orders
      ON orders.OrderId=item.OrderId AND orders.BusinessId=item.BusinessId
    LEFT JOIN dbo.Products product
      ON product.ProductId=item.ProductId AND product.BusinessId=item.BusinessId
    WHERE item.OrderId=@OrderId AND item.ProductId IS NOT NULL
    ORDER BY COALESCE(TRY_CONVERT(INT,JSON_VALUE(
             CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
             '$.LinePosition')),2147483647),item.CreatedAt,item.OrderItemId;
END
