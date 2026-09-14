CREATE PROCEDURE [dbo].[SellerOrderMetadataUpdate]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @Notes NVARCHAR(MAX) = NULL,
    @UserId UNIQUEIDENTIFIER,
    @WorkSessionId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE dbo.Orders
    SET Notes = @Notes,
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
