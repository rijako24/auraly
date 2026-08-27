CREATE PROCEDURE dbo.DispatchSettlementOperationComplete
    @Id UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @SettlementKey NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.DispatchSettlementOperations
        WHERE DispatchSettlementOperationId = @Id
          AND Status = N'Completed'
    )
        RETURN;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DocumentProcessingJobs job
        INNER JOIN
        (
            SELECT ReturnId AS DocumentId, N'SalesReturn' AS DocumentType
            FROM dbo.SalesReturns
            WHERE IdempotencyKey LIKE @SettlementKey
            UNION ALL
            SELECT PaymentId, N'ReceivablePayment'
            FROM dbo.CustomerPayments
            WHERE IdempotencyKey LIKE @SettlementKey
        ) settlementDocument
            ON settlementDocument.DocumentId = job.DocumentId
           AND settlementDocument.DocumentType = job.DocumentType
        WHERE job.Status <> N'Completed'
    )
        THROW 51000, 'Settlement documents are not fully processed.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DispatchSettlements settlement
        WHERE settlement.DispatchId = @DispatchId
          AND settlement.CashDifference <> 0
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.DocumentProcessingJobs job
              WHERE job.DocumentId = settlement.DispatchSettlementId
                AND job.DocumentType = N'DispatchCashDifference'
                AND job.Status = N'Completed'
          )
    )
        THROW 51000, 'The dispatch cash difference is not fully processed.', 1;

    UPDATE dbo.DispatchSettlementOperations
    SET Status = N'Completed',
        CompletedAt = SYSUTCDATETIME(),
        LastError = NULL
    WHERE DispatchSettlementOperationId = @Id;

    UPDATE dbo.DispatchSettlements
    SET Status = N'Completed'
    WHERE DispatchId = @DispatchId;

    UPDATE dbo.Dispatches
    SET Status = N'Closed',
        UpdatedBy = @UserId,
        UpdatedAt = SYSUTCDATETIME()
    WHERE DispatchId = @DispatchId;
END
