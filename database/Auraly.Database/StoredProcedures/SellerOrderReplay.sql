CREATE PROCEDURE [dbo].[SellerOrderReplay]
    @BusinessId UNIQUEIDENTIFIER,
    @IdempotencyKey NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT OrderId,ExternalDocumentNumber,Status,Total,ExternalStatus
    FROM dbo.Orders WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND IdempotencyKey=@IdempotencyKey;
END
