CREATE PROCEDURE [dbo].[SellerOrderEditableGet]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @WorkSessionId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT o.ExternalDocumentNumber,o.CustomerId,o.Status,
           COALESCE(o.WarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.WarehouseId'))),
           COALESCE(o.OrdersWarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.ordersWarehouseId')))
    FROM dbo.Orders o
    WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId AND o.Source=1
      AND (
        COALESCE(o.CapturedByUserId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.createdBy')))=@UserId
        OR EXISTS(
          SELECT 1 FROM dbo.OrderClaims claim
          WHERE claim.OrderId=o.OrderId AND claim.BusinessId=o.BusinessId
            AND claim.UserId=@UserId AND claim.WorkSessionId=@WorkSessionId
            AND claim.ReleasedAt IS NULL AND claim.ExpiresAt>SYSUTCDATETIME()))
      AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link WHERE link.OrderId=o.OrderId);

    SELECT ProductId,SUM(Quantity)
    FROM dbo.OrderItems
    WHERE OrderId=@OrderId AND ProductId IS NOT NULL
    GROUP BY ProductId;
END
