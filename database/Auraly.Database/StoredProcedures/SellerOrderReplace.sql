CREATE PROCEDURE [dbo].[SellerOrderReplace]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @PartySiteId UNIQUEIDENTIFIER,
    @CustomerName NVARCHAR(150),
    @CustomerIdentification NVARCHAR(80) = NULL,
    @CustomerEmail NVARCHAR(200) = NULL,
    @CustomerPhone NVARCHAR(50) = NULL,
    @CustomerAddress NVARCHAR(500) = NULL,
    @Notes NVARCHAR(MAX) = NULL,
    @Total DECIMAL(19,4),
    @ReservationTransferId UNIQUEIDENTIFIER,
    @Status INT,
    @ExternalStatus NVARCHAR(80),
    @RequiresStockReview BIT,
    @LinesJson NVARCHAR(MAX),
    @UserId UNIQUEIDENTIFIER,
    @WorkSessionId UNIQUEIDENTIFIER = NULL,
    @RequestedSellerId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @RequestedSellerId IS NOT NULL AND NOT EXISTS(
      SELECT 1 FROM dbo.Orders currentOrder WHERE currentOrder.OrderId=@OrderId
        AND currentOrder.BusinessId=@BusinessId AND currentOrder.SellerId=@RequestedSellerId)
    BEGIN
      IF NOT EXISTS(SELECT 1 FROM dbo.CommerceSellers seller JOIN dbo.Parties party
          ON party.PartyId=seller.PartyId AND party.TenantId=seller.TenantId
          JOIN dbo.Businesses business ON business.TenantId=seller.TenantId AND business.BusinessId=@BusinessId
          WHERE seller.SellerId=@RequestedSellerId AND seller.IsActive=1 AND party.IsActive=1)
        THROW 51300,N'El vendedor no está activo en esta empresa.',1;
      IF EXISTS(SELECT 1 FROM dbo.Orders orders JOIN dbo.SalesRoutes route ON route.RouteId=orders.RouteId
          WHERE orders.OrderId=@OrderId AND orders.BusinessId=@BusinessId AND route.SellerId<>@RequestedSellerId)
        THROW 51300,N'El vendedor seleccionado debe coincidir con la ruta del pedido.',1;
    END;

    DECLARE @Subtotal DECIMAL(19,4), @DiscountTotal DECIMAL(19,4);
    SELECT @Subtotal=SUM(j.Quantity*j.UnitPrice),
           @DiscountTotal=SUM(j.DiscountAmount)
    FROM OPENJSON(@LinesJson) WITH(
        Quantity DECIMAL(19,6) '$.quantity',UnitPrice DECIMAL(19,4) '$.unitPrice',
        DiscountAmount DECIMAL(19,4) '$.discountAmount') j;

    UPDATE dbo.Orders
    SET SellerId=COALESCE(@RequestedSellerId,SellerId), CustomerId=@CustomerId,PartySiteId=@PartySiteId,CustomerNameSnapshot=@CustomerName,
        CustomerDocumentSnapshot=@CustomerIdentification,CustomerEmailSnapshot=@CustomerEmail,
        CustomerPhoneSnapshot=@CustomerPhone,DeliveryAddressSnapshot=@CustomerAddress,
        Notes=@Notes,Subtotal=@Subtotal,DiscountTotal=@DiscountTotal,Total=@Total,
        Status=@Status,ExternalStatus=@ExternalStatus,
        ReservationTransferId=@ReservationTransferId,
        ReleaseTransferId=NULL,
        RequiresStockReview=@RequiresStockReview,
        UpdatedAt=SYSUTCDATETIME()
    WHERE OrderId=@OrderId AND BusinessId=@BusinessId AND Status IN(2,5)
      AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link WHERE link.OrderId=@OrderId);
    IF @@ROWCOUNT <> 1 THROW 51303, 'No se pudo actualizar el pedido.', 1;

    DELETE dbo.OrderItems WHERE OrderId=@OrderId;
    INSERT dbo.OrderItems(OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,ProductNameSnapshot,
        DescriptionSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,DocumentUnitCost,IsGenericProductSnapshot,DiscountAmount,TaxAmount,LineTotal,RawPayloadJson,CreatedAt)
    SELECT NEWID(),@OrderId,@BusinessId,j.ProductId,j.Code,j.Code,j.Name,j.Name,j.UnitCode,j.Quantity,j.UnitPrice,j.DocumentUnitCost,j.IsGenericProductSnapshot,j.DiscountAmount,j.TaxAmount,j.LineTotal,j.RawPayloadJson,SYSUTCDATETIME()
    FROM OPENJSON(@LinesJson) WITH(
        ProductId UNIQUEIDENTIFIER '$.productId',Code NVARCHAR(100) '$.code',Name NVARCHAR(250) '$.name',
        UnitCode NVARCHAR(24) '$.unitCode',Quantity DECIMAL(19,6) '$.quantity',UnitPrice DECIMAL(19,4) '$.unitPrice',DocumentUnitCost DECIMAL(19,6) '$.documentUnitCost',IsGenericProductSnapshot BIT '$.isGenericProductSnapshot',DiscountAmount DECIMAL(19,4) '$.discountAmount',
        LineTotal DECIMAL(19,4) '$.lineTotal',TaxAmount DECIMAL(19,4) '$.taxAmount',
        RawPayloadJson NVARCHAR(MAX) '$.rawPayloadJson') j;
    IF @@ROWCOUNT = 0 THROW 51304, 'El pedido requiere al menos un producto.', 1;

    IF @WorkSessionId IS NOT NULL
    BEGIN
        UPDATE dbo.OrderClaims
        SET ReleasedAt=COALESCE(ReleasedAt,SYSUTCDATETIME())
        WHERE OrderId=@OrderId AND BusinessId=@BusinessId
          AND WorkSessionId=@WorkSessionId AND UserId=@UserId
          AND ReleasedAt IS NULL;
    END
END
