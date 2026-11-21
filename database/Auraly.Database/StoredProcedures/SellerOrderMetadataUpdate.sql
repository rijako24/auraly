CREATE PROCEDURE [dbo].[SellerOrderMetadataUpdate]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @Notes NVARCHAR(MAX) = NULL,
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


    UPDATE dbo.Orders
    SET SellerId=COALESCE(@RequestedSellerId,SellerId), Notes = @Notes,
        UpdatedAt = SYSUTCDATETIME()
    WHERE OrderId = @OrderId
      AND BusinessId = @BusinessId
      AND Status IN (2, 5)
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.OrderInvoiceLinks link
          WHERE link.OrderId = @OrderId);

    IF @@ROWCOUNT <> 1
        THROW 51303, 'No se pudo actualizar el pedido.', 1;

    IF @WorkSessionId IS NOT NULL
    BEGIN
        UPDATE dbo.OrderClaims
        SET ReleasedAt = COALESCE(ReleasedAt, SYSUTCDATETIME())
        WHERE OrderId = @OrderId
          AND BusinessId = @BusinessId
          AND WorkSessionId = @WorkSessionId
          AND UserId = @UserId
          AND ReleasedAt IS NULL;
    END
END
