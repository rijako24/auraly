CREATE PROCEDURE dbo.DispatchSettlementOperationClaimMark
    @Id UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DispatchSettlementOperations
    SET Status = N'Processing',
        Attempts = Attempts + 1,
        NextAttemptAt = DATEADD(MINUTE, 5, SYSUTCDATETIME()),
        LastError = NULL
    WHERE DispatchSettlementOperationId = @Id;

    UPDATE dbo.Dispatches
    SET Status = N'SettlementProcessing',
        UpdatedAt = SYSUTCDATETIME()
    WHERE DispatchId = @DispatchId
      AND Status = N'SettlementAttention';
END
