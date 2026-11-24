CREATE PROCEDURE dbo.DispatchSettlementOperationComplete
    @Id UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    -- All mutations of a settlement take the aggregate lock first.
    IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@DispatchId)
        THROW 51000,N'El despacho no existe.',1;
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
        FROM dbo.Dispatches dispatch
        CROSS APPLY dispatch.SettlementDocuments(dispatch.DispatchId,dispatch.BusinessId) settlementDocument
        LEFT JOIN dbo.DocumentProcessingJobs job ON settlementDocument.DocumentId = job.DocumentId
           AND settlementDocument.DocumentType = job.DocumentType AND job.BusinessId=dispatch.BusinessId
           AND settlementDocument.DocumentType=N'SalesReturn'
        LEFT JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=settlementDocument.DocumentId
          AND financial.SourceDocumentType=settlementDocument.DocumentType
          AND financial.TenantId=dispatch.TenantId AND financial.BusinessId=dispatch.BusinessId
        -- Portfolio payments enter the financial engine directly. Only returns
        -- have an operational inventory document before their financial effects.
        WHERE dispatch.DispatchId=@DispatchId AND
          (settlementDocument.DocumentType=N'SalesReturn' AND (job.Status IS NULL OR job.Status<>N'Completed')
            OR financial.Status IS NULL OR financial.Status NOT IN(N'Posted',N'CommercialEffectsApplied'))
    )
        THROW 51000, N'Los documentos de la liquidación aún no terminaron sus efectos financieros.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dispatch.Closures settlement
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
        THROW 51000, N'La diferencia de efectivo del despacho aún no terminó de procesarse.', 1;

    IF NOT EXISTS(SELECT 1 FROM dispatch.Closures closure
      JOIN dbo.AccountingPostingJobs posting ON posting.SourceDocumentId=closure.DispatchSettlementId
        AND posting.SourceDocumentType=N'DispatchCashDifference' AND posting.BusinessId=closure.BusinessId
      WHERE closure.DispatchId=@DispatchId AND posting.Status IN(N'Posted',N'CommercialEffectsApplied'))
      THROW 51000,N'La conciliación financiera del despacho aún no terminó.',1;

    UPDATE dbo.DispatchSettlementOperations
    SET Status = N'Completed',
        CompletedAt = SYSUTCDATETIME(),
        LastError = NULL
    WHERE DispatchSettlementOperationId = @Id;

    UPDATE closure SET DispatchStatus=N'Completed',ReconciliationStatus=CASE WHEN viewdata.CashDifference=0
      AND (viewdata.DeclaredCard IS NULL OR viewdata.DeclaredCard=payments.CardTotal)
      AND (viewdata.DeclaredTransfer IS NULL OR viewdata.DeclaredTransfer=payments.TransferTotal)
      THEN N'Reconciled' ELSE N'ReconciledWithDifferences' END
    FROM dbo.WorkSessionClosures closure JOIN dispatch.Closures viewdata ON viewdata.DispatchSettlementId=closure.WorkSessionClosureId
    CROSS APPLY dispatch.PaymentTotals(@DispatchId) payments
    WHERE viewdata.DispatchId=@DispatchId;

    UPDATE dbo.Dispatches
    SET Status = N'Closed',
        UpdatedBy = @UserId,
        UpdatedAt = SYSUTCDATETIME()
    WHERE DispatchId = @DispatchId;
END
