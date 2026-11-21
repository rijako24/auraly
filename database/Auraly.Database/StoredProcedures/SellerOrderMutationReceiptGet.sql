CREATE PROCEDURE [dbo].[SellerOrderMutationReceiptGet]
    @BusinessId UNIQUEIDENTIFIER,
    @IdempotencyKey NVARCHAR(160)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT OrderId, UserId, RequestHash, ResultJson
    FROM dbo.OrderMutationReceipts WITH (UPDLOCK, HOLDLOCK)
    WHERE BusinessId = @BusinessId AND IdempotencyKey = @IdempotencyKey;
END;
