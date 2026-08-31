CREATE PROCEDURE [purchasing].[ReceiptOrderValidate]
    @BusinessId UNIQUEIDENTIFIER,
    @PurchaseOrderId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @SupplierId UNIQUEIDENTIFIER,
    @CanAuthorizeOverReceipt BIT,
    @LinesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS
    (
        SELECT 1 FROM purchasing.PurchaseOrders WITH(UPDLOCK,HOLDLOCK)
        WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId
          AND WarehouseId=@WarehouseId AND SupplierId=@SupplierId
          AND Status IN(N'Open',N'PartiallyReceived')
    ) THROW 51209,'The purchase order is unavailable, closed or belongs to another supplier or warehouse.',1;

    SELECT PurchaseOrderLineId,ProductId,Quantity,OverReceiptReason
    INTO #Requested
    FROM OPENJSON(@LinesJson) WITH
    (
        PurchaseOrderLineId uniqueidentifier '$.PurchaseOrderLineId',
        ProductId uniqueidentifier '$.ProductId',
        Quantity decimal(19,6) '$.Quantity',
        OverReceiptReason nvarchar(500) '$.OverReceiptReason'
    );

    IF EXISTS(SELECT 1 FROM #Requested WHERE PurchaseOrderLineId IS NULL)
        THROW 51209,'Every recovered receipt line requires PurchaseOrderLineId.',1;
    IF EXISTS(SELECT PurchaseOrderLineId FROM #Requested GROUP BY PurchaseOrderLineId HAVING COUNT(*)>1)
        THROW 51209,'A purchase-order line can appear only once in a receipt.',1;
    IF EXISTS
    (
        SELECT 1 FROM #Requested requested
        LEFT JOIN purchasing.PurchaseOrderLines line WITH(UPDLOCK,HOLDLOCK)
          ON line.PurchaseOrderId=@PurchaseOrderId AND line.LineId=requested.PurchaseOrderLineId
        WHERE line.LineId IS NULL OR line.ProductId<>requested.ProductId
    ) THROW 51209,'A receipt line does not match the selected purchase order.',1;

    SELECT requested.PurchaseOrderLineId,requested.OverReceiptReason,requested.Quantity,
        CASE WHEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity-COALESCE(pending.Quantity,0)>0
             THEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity-COALESCE(pending.Quantity,0) ELSE 0 END RemainingQuantity
    INTO #Evaluated
    FROM #Requested requested
    JOIN purchasing.PurchaseOrderLines line WITH(UPDLOCK,HOLDLOCK)
      ON line.PurchaseOrderId=@PurchaseOrderId AND line.LineId=requested.PurchaseOrderLineId
    OUTER APPLY
    (
        SELECT SUM(receiptLine.Quantity) Quantity
        FROM dbo.GoodsReceiptLines receiptLine WITH(UPDLOCK,HOLDLOCK)
        JOIN dbo.GoodsReceipts receipt WITH(UPDLOCK,HOLDLOCK) ON receipt.GoodsReceiptId=receiptLine.GoodsReceiptId
        WHERE receiptLine.PurchaseOrderLineId=line.LineId AND receipt.Status=N'Accepted'
    ) pending;

    IF EXISTS(SELECT 1 FROM #Evaluated WHERE Quantity>RemainingQuantity AND NULLIF(LTRIM(RTRIM(OverReceiptReason)),N'') IS NULL)
        THROW 51209,'Receiving above the pending quantity requires a reason.',1;
    IF EXISTS(SELECT 1 FROM #Evaluated WHERE Quantity<=RemainingQuantity AND OverReceiptReason IS NOT NULL)
        THROW 51209,'OverReceiptReason is only valid when quantity exceeds the order balance.',1;
    IF @CanAuthorizeOverReceipt=0 AND EXISTS(SELECT 1 FROM #Evaluated WHERE Quantity>RemainingQuantity)
        THROW 51211,'Receiving above the pending quantity requires the over-receipt permission.',1;

    SELECT PurchaseOrderLineId FROM #Evaluated WHERE Quantity>RemainingQuantity;
END;
