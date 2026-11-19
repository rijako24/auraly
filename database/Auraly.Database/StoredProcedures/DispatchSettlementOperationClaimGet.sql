CREATE PROCEDURE dbo.DispatchSettlementOperationClaimGet
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    DECLARE @Claimed TABLE(Id uniqueidentifier,BusinessId uniqueidentifier,DispatchId uniqueidentifier,
      WorkSessionId uniqueidentifier,RequestedBy uniqueidentifier,RequestedAt datetimeoffset(7),Attempts int);
    -- The UPDATE is the atomic lease claim. Its autocommit releases the queue
    -- lock before reading the dispatch; no nested transaction is required.
    ;WITH candidate AS
    (
        SELECT TOP(1) operation.* FROM dbo.DispatchSettlementOperations operation WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK)
        WHERE operation.Status IN(N'Pending',N'Processing') AND operation.NextAttemptAt<=SYSUTCDATETIME()
          AND (operation.Status=N'Pending' OR NOT EXISTS(SELECT 1 FROM dbo.WorkSessions session
            JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
            JOIN dbo.AccountingPostingJobs posting ON posting.SourceDocumentId=closure.WorkSessionClosureId
              AND posting.SourceDocumentType=N'DispatchCashDifference' AND posting.BusinessId=operation.BusinessId
            WHERE session.DispatchId=operation.DispatchId))
        ORDER BY operation.NextAttemptAt,operation.RequestedAt
    )
    UPDATE candidate SET Status=N'Processing',Attempts=Attempts+1,
      NextAttemptAt=DATEADD(MINUTE,5,SYSUTCDATETIME()),LastError=NULL
    OUTPUT inserted.DispatchSettlementOperationId,inserted.BusinessId,inserted.DispatchId,
      inserted.WorkSessionId,inserted.RequestedBy,inserted.RequestedAt,inserted.Attempts INTO @Claimed;
    -- Release the queue lock before reading the dispatch. Commands lock the
    -- dispatch first; holding both in the opposite order deadlocks replay.
    SELECT operation.Id,operation.BusinessId,operation.DispatchId,operation.WorkSessionId,
           operation.RequestedBy,operation.RequestedAt,operation.Attempts,
           business.TenantId,
           dispatch.DispatchNumber,
           settlement.DispatchSettlementId,
           dispatch.DriverName
    FROM @Claimed operation
    INNER JOIN dbo.Businesses business ON business.BusinessId = operation.BusinessId
    INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId = operation.DispatchId
    INNER JOIN dispatch.Closures settlement ON settlement.DispatchId = operation.DispatchId
    ;
    -- One deadline for a known pending/abandoned lease, not periodic polling.
    -- Once the canonical accounting job exists it owns further progress.
    IF NOT EXISTS(SELECT 1 FROM @Claimed)
      SELECT MIN(operation.NextAttemptAt),TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00')
      FROM dbo.DispatchSettlementOperations operation WITH(READPAST,READCOMMITTEDLOCK)
      WHERE operation.Status IN(N'Pending',N'Processing')
        AND (operation.Status=N'Pending' OR NOT EXISTS(SELECT 1 FROM dbo.WorkSessions session
          JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
          JOIN dbo.AccountingPostingJobs posting ON posting.SourceDocumentId=closure.WorkSessionClosureId
            AND posting.SourceDocumentType=N'DispatchCashDifference' AND posting.BusinessId=operation.BusinessId
          WHERE session.DispatchId=operation.DispatchId));
END
