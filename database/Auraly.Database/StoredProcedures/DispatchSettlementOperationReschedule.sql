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
    IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@DispatchId)
        THROW 51000,N'El despacho no existe.',1;
    UPDATE dbo.DispatchSettlementOperations
    SET Status = @OperationStatus,
        NextAttemptAt = SYSUTCDATETIME(),
        LastError = @Error
    WHERE DispatchSettlementOperationId = @Id
      AND DispatchId=@DispatchId AND Status = N'Processing';
    IF @@ROWCOUNT=0 RETURN;

    UPDATE dbo.Dispatches
    SET Status = @DispatchStatus,
        UpdatedAt = SYSUTCDATETIME()
    WHERE DispatchId = @DispatchId;

    UPDATE dispatch.Closures
    SET Status = @SettlementStatus
    WHERE DispatchId = @DispatchId;
END
