CREATE PROCEDURE [dbo].[SellerOrderReplace]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @Notes NVARCHAR(MAX) = NULL,
    @Total DECIMAL(19,4),
    @ReservationTransferId UNIQUEIDENTIFIER,
    @LinesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    UPDATE dbo.Orders
    SET Notes=@Notes,Subtotal=@Total,Total=@Total,Status=2,ExternalStatus=N'InventoryTransferAccepted',
        CustomAttributesJson=JSON_MODIFY(JSON_MODIFY(CustomAttributesJson,'$.reservationTransferId',CONVERT(nvarchar(36),@ReservationTransferId)),'$.requiresStockReview',CAST(0 AS bit)),
        UpdatedAt=SYSUTCDATETIME()
    WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
    IF @@ROWCOUNT <> 1 THROW 51303, 'No se pudo actualizar el pedido.', 1;

    DELETE dbo.OrderItems WHERE OrderId=@OrderId;
    INSERT dbo.OrderItems(OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,ProductNameSnapshot,
        DescriptionSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,DiscountAmount,TaxAmount,LineTotal,RawPayloadJson,CreatedAt)
    SELECT NEWID(),@OrderId,@BusinessId,j.ProductId,j.Code,j.Code,j.Name,j.Name,j.UnitCode,j.Quantity,j.UnitPrice,0,0,j.LineTotal,j.RawPayloadJson,SYSUTCDATETIME()
    FROM OPENJSON(@LinesJson) WITH(
        ProductId UNIQUEIDENTIFIER '$.productId',Code NVARCHAR(100) '$.code',Name NVARCHAR(250) '$.name',
        UnitCode NVARCHAR(24) '$.unitCode',Quantity DECIMAL(19,6) '$.quantity',UnitPrice DECIMAL(19,4) '$.unitPrice',
        LineTotal DECIMAL(19,4) '$.lineTotal',RawPayloadJson NVARCHAR(MAX) '$.rawPayloadJson') j;
    IF @@ROWCOUNT = 0 THROW 51304, 'El pedido requiere al menos un producto.', 1;
END
