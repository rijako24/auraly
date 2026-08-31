CREATE PROCEDURE [purchasing].[PurchaseOrderClose]
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @PurchaseOrderId UNIQUEIDENTIFIER,
    @RowVersion VARBINARY(8),
    @Reason NVARCHAR(500),
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM purchasing.PurchaseOrders WITH(UPDLOCK,HOLDLOCK) WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId)
        THROW 51200,'Purchase order was not found.',1;
    IF NOT EXISTS(SELECT 1 FROM purchasing.PurchaseOrders WHERE PurchaseOrderId=@PurchaseOrderId AND RowVersion=@RowVersion)
        THROW 51204,'The purchase order changed in another session.',1;
    IF NOT EXISTS(SELECT 1 FROM purchasing.PurchaseOrders WHERE PurchaseOrderId=@PurchaseOrderId AND Status IN(N'Open',N'PartiallyReceived'))
        THROW 51205,'Only an open purchase order can be closed.',1;
    IF EXISTS(SELECT 1 FROM dbo.GoodsReceipts WITH(UPDLOCK,HOLDLOCK) WHERE PurchaseOrderId=@PurchaseOrderId AND Status=N'Accepted')
        THROW 51206,'The purchase order has a receipt pending processing.',1;

    UPDATE purchasing.PurchaseOrderLines
    SET CancelledQuantity=CASE WHEN OrderedQuantity>ReceivedQuantity THEN OrderedQuantity-ReceivedQuantity ELSE 0 END
    WHERE PurchaseOrderId=@PurchaseOrderId;
    UPDATE purchasing.PurchaseOrders
    SET Status=N'Closed',CloseReason=@Reason,ClosedByUserId=@UserId,ClosedAt=@Now,UpdatedAt=@Now
    WHERE PurchaseOrderId=@PurchaseOrderId;
END;
