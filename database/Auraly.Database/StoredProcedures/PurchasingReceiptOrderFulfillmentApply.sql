CREATE PROCEDURE [purchasing].[ReceiptOrderFulfillmentApply]
    @BusinessId UNIQUEIDENTIFIER,
    @PurchaseOrderId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @SupplierId UNIQUEIDENTIFIER,
    @LinesJson NVARCHAR(MAX),
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS
    (
        SELECT 1 FROM purchasing.PurchaseOrders WITH(UPDLOCK,HOLDLOCK)
        WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId AND WarehouseId=@WarehouseId
          AND SupplierId=@SupplierId AND Status IN(N'Open',N'PartiallyReceived')
    ) THROW 51220,'The purchase order is no longer receivable.',1;

    SELECT PurchaseOrderLineId,ProductId,Quantity,OverReceiptAuthorized
    INTO #Lines
    FROM OPENJSON(@LinesJson) WITH
    (
        PurchaseOrderLineId uniqueidentifier '$.PurchaseOrderLineId',
        ProductId uniqueidentifier '$.ProductId',
        Quantity decimal(19,6) '$.Quantity',
        OverReceiptAuthorized bit '$.OverReceiptAuthorized'
    );

    IF EXISTS
    (
        SELECT 1 FROM #Lines input
        LEFT JOIN purchasing.PurchaseOrderLines line WITH(UPDLOCK,HOLDLOCK)
          ON line.PurchaseOrderId=@PurchaseOrderId AND line.LineId=input.PurchaseOrderLineId
        WHERE line.LineId IS NULL OR line.ProductId<>input.ProductId
    ) THROW 51221,'The receipt line no longer matches the purchase order.',1;
    IF EXISTS
    (
        SELECT 1 FROM #Lines input
        JOIN purchasing.PurchaseOrderLines line WITH(UPDLOCK,HOLDLOCK)
          ON line.PurchaseOrderId=@PurchaseOrderId AND line.LineId=input.PurchaseOrderLineId
        WHERE line.ReceivedQuantity+input.Quantity>line.OrderedQuantity-line.CancelledQuantity
          AND input.OverReceiptAuthorized=0
    ) THROW 51222,'The receipt exceeds the pending order quantity without authorization.',1;

    UPDATE line SET ReceivedQuantity=line.ReceivedQuantity+input.Quantity
    FROM purchasing.PurchaseOrderLines line
    JOIN #Lines input ON input.PurchaseOrderLineId=line.LineId
    WHERE line.PurchaseOrderId=@PurchaseOrderId;

    UPDATE purchasing.PurchaseOrders
    SET Status=CASE WHEN NOT EXISTS
        (SELECT 1 FROM purchasing.PurchaseOrderLines line WHERE line.PurchaseOrderId=@PurchaseOrderId
          AND line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity>0)
        THEN N'Received' ELSE N'PartiallyReceived' END,
        UpdatedAt=@Now
    WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId;
END;
