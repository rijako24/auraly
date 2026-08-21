CREATE PROCEDURE [dbo].[SellerOrderConfirm]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @ExternalStatus NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Orders
    SET Status=2,ExternalStatus=@ExternalStatus,UpdatedAt=SYSUTCDATETIME()
    WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
    IF @@ROWCOUNT <> 1 THROW 51302, 'No se pudo confirmar el pedido.', 1;
END
