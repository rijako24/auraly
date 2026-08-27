CREATE PROCEDURE dbo.DispatchSettlementCashDifferenceGet
    @SettlementId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ExpectedCash, CashReceived, ReceivedAt, Notes
    FROM dbo.DispatchSettlements WITH (UPDLOCK, HOLDLOCK)
    WHERE DispatchSettlementId = @SettlementId
      AND BusinessId = @BusinessId
      AND DispatchId = @DispatchId
      AND Status = N'Processing';
END
