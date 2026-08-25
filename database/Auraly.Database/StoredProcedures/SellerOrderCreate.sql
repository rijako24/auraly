CREATE PROCEDURE [dbo].[SellerOrderCreate]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @OrdersWarehouseId UNIQUEIDENTIFIER,
    @ReservationTransferId UNIQUEIDENTIFIER,
    @RouteId UNIQUEIDENTIFIER = NULL,
    @RouteStopId UNIQUEIDENTIFIER = NULL,
    @PartySiteId UNIQUEIDENTIFIER = NULL,
    @CapturedByUserId UNIQUEIDENTIFIER,
    @CapturedOffline BIT,
    @RequiresStockReview BIT,
    @Status INT,
    @CustomerName NVARCHAR(200),
    @Email NVARCHAR(254) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @Identification NVARCHAR(80) = NULL,
    @Address NVARCHAR(500),
    @Notes NVARCHAR(MAX) = NULL,
    @Total DECIMAL(19,4),
    @Number NVARCHAR(300),
    @ExternalStatus NVARCHAR(100),
    @IdempotencyKey NVARCHAR(200),
    @LinesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @SellerId UNIQUEIDENTIFIER=(
      SELECT seller.SellerId
      FROM dbo.AppUsers appUser
      INNER JOIN dbo.CommerceSellers seller
        ON seller.PartyId=appUser.PartyId AND seller.BusinessId=@BusinessId AND seller.IsActive=1
      WHERE appUser.UserId=@CapturedByUserId);
    IF @SellerId IS NULL THROW 51300,'El usuario autenticado no tiene un vendedor comercial activo.',1;
    IF @RouteId IS NOT NULL AND NOT EXISTS(
      SELECT 1 FROM dbo.SalesRoutes route
      WHERE route.RouteId=@RouteId AND route.BusinessId=@BusinessId
        AND route.SellerId=@SellerId AND route.IsActive=1)
      THROW 51300,'La ruta no pertenece al vendedor autenticado.',1;
    IF @RouteStopId IS NOT NULL AND NOT EXISTS(
      SELECT 1 FROM dbo.SalesRouteStops stop
      WHERE stop.RouteStopId=@RouteStopId AND stop.RouteId=@RouteId
        AND stop.CustomerId=@CustomerId AND stop.PartySiteId=@PartySiteId AND stop.IsActive=1)
      THROW 51300,'La parada no corresponde a la ruta, cliente y sede seleccionados.',1;

    INSERT dbo.Orders(OrderId,BusinessId,CustomerId,WarehouseId,OrdersWarehouseId,ReservationTransferId,
        SellerId,RouteId,RouteStopId,PartySiteId,CapturedByUserId,CapturedOffline,RequiresStockReview,
        Source,FulfillmentMode,Status,CustomerNameSnapshot,
        CustomerEmailSnapshot,CustomerPhoneSnapshot,CustomerDocumentSnapshot,DeliveryAddressSnapshot,Notes,
        Currency,Subtotal,DiscountTotal,TaxTotal,Total,CustomerConfirmed,ExternalDocumentNumber,ExternalStatus,
        IdempotencyKey,CreatedAt,UpdatedAt)
    VALUES(@OrderId,@BusinessId,@CustomerId,@WarehouseId,@OrdersWarehouseId,@ReservationTransferId,
        @SellerId,@RouteId,@RouteStopId,@PartySiteId,@CapturedByUserId,@CapturedOffline,@RequiresStockReview,
        1,0,@Status,@CustomerName,@Email,@Phone,@Identification,@Address,@Notes,
        N'COP',@Total,0,0,@Total,1,@Number,@ExternalStatus,@IdempotencyKey,SYSUTCDATETIME(),SYSUTCDATETIME());

    INSERT dbo.OrderItems(OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,ProductNameSnapshot,
        DescriptionSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,DiscountAmount,TaxAmount,LineTotal,RawPayloadJson,CreatedAt)
    SELECT NEWID(),@OrderId,@BusinessId,j.ProductId,j.Code,j.Code,j.Name,j.Name,j.UnitCode,j.Quantity,j.UnitPrice,0,j.TaxAmount,j.LineTotal,j.RawPayloadJson,SYSUTCDATETIME()
    FROM OPENJSON(@LinesJson) WITH(
        ProductId UNIQUEIDENTIFIER '$.productId',
        Code NVARCHAR(100) '$.code',
        Name NVARCHAR(250) '$.name',
        UnitCode NVARCHAR(24) '$.unitCode',
        Quantity DECIMAL(19,6) '$.quantity',
        UnitPrice DECIMAL(19,4) '$.unitPrice',
        TaxAmount DECIMAL(19,4) '$.taxAmount',
        LineTotal DECIMAL(19,4) '$.lineTotal',
        RawPayloadJson NVARCHAR(MAX) '$.rawPayloadJson') j;

    IF @@ROWCOUNT = 0 THROW 51301, 'El pedido requiere al menos un producto.', 1;
END
