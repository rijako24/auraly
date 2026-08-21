CREATE PROCEDURE [dbo].[SellerOrderEditableGet]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT o.ExternalDocumentNumber,o.CustomerId,o.Status,
           TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.WarehouseId')),
           TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.ordersWarehouseId'))
    FROM dbo.Orders o
    WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId AND o.Source=1
      AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.createdBy'))=@UserId
      AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link WHERE link.OrderId=o.OrderId);

    SELECT ProductId,SUM(Quantity)
    FROM dbo.OrderItems
    WHERE OrderId=@OrderId AND ProductId IS NOT NULL
    GROUP BY ProductId;
END
