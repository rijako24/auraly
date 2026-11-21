CREATE PROCEDURE [dbo].[SellerOrderMutationReceiptSave]
    @BusinessId UNIQUEIDENTIFIER,
    @OrderId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @IdempotencyKey NVARCHAR(160),
    @RequestHash CHAR(64),
    @ResultJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.OrderMutationReceipts
        (BusinessId, OrderId, UserId, IdempotencyKey, RequestHash, ResultJson, CreatedAt)
    VALUES
        (@BusinessId, @OrderId, @UserId, @IdempotencyKey, @RequestHash, @ResultJson, SYSDATETIMEOFFSET());
END;
