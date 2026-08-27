CREATE PROCEDURE dbo.DispatchSettlementOperationReschedule
    @Id UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER,
    @OperationStatus NVARCHAR(32),
    @DispatchStatus NVARCHAR(32),
    @SettlementStatus NVARCHAR(32),
    @Error NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DispatchSettlementOperations
    SET Status = @OperationStatus,
        NextAttemptAt = SYSUTCDATETIME(),
        LastError = @Error
    WHERE DispatchSettlementOperationId = @Id
      AND Status = N'Processing';

    UPDATE dbo.Dispatches
    SET Status = @DispatchStatus,
        UpdatedAt = SYSUTCDATETIME()
    WHERE DispatchId = @DispatchId;

    UPDATE dbo.DispatchSettlements
    SET Status = @SettlementStatus
    WHERE DispatchId = @DispatchId;
END
