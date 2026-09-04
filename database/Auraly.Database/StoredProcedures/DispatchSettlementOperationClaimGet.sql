CREATE PROCEDURE dbo.DispatchSettlementOperationClaimGet
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1)
           operation.DispatchSettlementOperationId,
           operation.BusinessId,
           operation.DispatchId,
           operation.WorkSessionId,
           operation.RequestedBy,
           operation.RequestedAt,
           operation.Attempts,
           business.TenantId,
           dispatch.WarehouseId,
           dispatch.DispatchNumber,
           settlement.DispatchSettlementId,
           dispatch.DriverName
    FROM dbo.DispatchSettlementOperations operation WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
    INNER JOIN dbo.Businesses business ON business.BusinessId = operation.BusinessId
    INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId = operation.DispatchId
    INNER JOIN dbo.DispatchSettlements settlement ON settlement.DispatchId = operation.DispatchId
    WHERE operation.Status IN (N'Pending', N'Processing')
      AND operation.NextAttemptAt <= SYSUTCDATETIME()
    ORDER BY operation.NextAttemptAt, operation.RequestedAt;
END
