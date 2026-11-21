CREATE FUNCTION accounting.SalePaymentPostingFacts(@TenantId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@DocumentId UNIQUEIDENTIFIER)
RETURNS TABLE
AS RETURN
(
    -- The original side of a reversal/reclassification is historical evidence,
    -- never the current account mapping. The job stores only bindings it applied.
    SELECT sale.DocumentId,payment.PaymentNumber,payment.MethodCode,
      payment.Amount+payment.RoundingAdjustment Amount,job.AccountingEntryRequired,job.Status PostingStatus,
      COALESCE(payment.AccountId,bank.AccountId,mapping.AccountId) AccountId,
      COALESCE(reversed.Amount,0) ReversedUncollectedAmount,
      CASE WHEN correction.ProofCount=0 THEN payment.MethodCode ELSE correction.MethodCode END EffectiveMethodCode,
      CASE WHEN correction.ProofCount=0 THEN payment.Amount+payment.RoundingAdjustment ELSE correction.Amount END EffectiveAmount,
      CASE WHEN correction.ProofCount=0 THEN payment.CardFranchiseCode ELSE correction.CardFranchiseCode END EffectiveCardFranchiseCode,
      CASE WHEN correction.ProofCount=0 THEN payment.ApprovalNumber ELSE correction.ApprovalNumber END EffectiveApprovalNumber,
      CASE WHEN correction.ProofCount=0 THEN COALESCE(payment.AccountId,bank.AccountId,mapping.AccountId)
        ELSE TRY_CONVERT(uniqueidentifier,correction.AccountId) END EffectiveAccountId,
      CAST(CASE WHEN correction.ProofCount=0 OR (correction.ProofCount=1
        AND (correction.AccountId IS NOT NULL OR (correction.ConfiguredPayment=0 AND correction.AccountingEntryRequired=0))
        AND correction.PostingStatus IN(N'Posted',N'CommercialEffectsApplied'))
        THEN 1 ELSE 0 END AS bit) EffectivePaymentReady,
      CASE WHEN correction.ProofCount=0 THEN payment.PaymentMethodName ELSE correction.PaymentMethodName END EffectiveFormName
    FROM dbo.SalesDocuments sale
    JOIN dbo.Businesses business ON business.BusinessId=sale.BusinessId AND business.TenantId=@TenantId
    JOIN dbo.SalesPayments payment ON payment.DocumentId=sale.DocumentId
    LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=sale.DocumentId
      AND job.SourceDocumentType=sale.DocumentType AND job.TenantId=@TenantId AND job.BusinessId=@BusinessId
    OUTER APPLY accounting.PaymentPostingCategory(N'PosPaymentMethod',payment.MethodCode) postingCategory
    OUTER APPLY(SELECT applied.AccountId FROM OPENJSON(job.ResolvedAccountMappingsJson)
      WITH(Category NVARCHAR(128),AccountId UNIQUEIDENTIFIER) applied WHERE applied.Category=postingCategory.Category) mapping
    OUTER APPLY(SELECT applied.AccountId FROM OPENJSON(job.ResolvedBankAccountsJson)
      WITH(BankAccountId UNIQUEIDENTIFIER,AccountId UNIQUEIDENTIFIER) applied WHERE applied.BankAccountId=payment.BankAccountId) bank
    OUTER APPLY(SELECT SUM(settlement.Amount) Amount FROM dbo.SalesReturnSettlements settlement
      JOIN dbo.SalesReturns returned ON returned.ReturnId=settlement.ReturnId AND returned.BusinessId=@BusinessId
      WHERE settlement.OriginalDocumentId=sale.DocumentId AND settlement.OriginalPaymentNumber=payment.PaymentNumber
        AND settlement.SettlementType=N'ReverseUncollected') reversed
    -- One reconciliation per session (schema-enforced). Keep the original
    -- auxiliary above for reversals, and expose the actual corrected tender
    -- separately for subsequent refunds. Never resolve it from today's catalog.
    OUTER APPLY(
      SELECT COUNT_BIG(*) ProofCount,MAX(corrected.TenderMethodCode) MethodCode,MAX(corrected.Amount) Amount,
        MAX(corrected.CardFranchiseCode) CardFranchiseCode,MAX(corrected.ApprovalNumber) ApprovalNumber,
        MAX(COALESCE(binding.Name,corrected.PaymentMethodName)) PaymentMethodName,
        MAX(posted.Status) PostingStatus,MAX(CONVERT(int,posted.AccountingEntryRequired)) AccountingEntryRequired,
        MAX(CASE WHEN corrected.PaymentMethodId IS NOT NULL THEN 1 ELSE 0 END) ConfiguredPayment,
        MAX(CONVERT(nvarchar(36),CASE WHEN corrected.PaymentMethodId IS NOT NULL THEN
          CASE WHEN binding.ProofCount=1 THEN TRY_CONVERT(uniqueidentifier,binding.AccountId) END
          ELSE COALESCE(CASE WHEN corrected.TenderCategory LIKE N'PostedAccount:%'
            THEN TRY_CONVERT(uniqueidentifier,SUBSTRING(corrected.TenderCategory,15,36)) END,target.AccountId) END)) AccountId
      FROM dbo.WorkSessionClosures closure
      JOIN dbo.WorkSessionClosureReconciliations reconciliation ON reconciliation.WorkSessionClosureId=closure.WorkSessionClosureId
      CROSS APPLY OPENJSON(reconciliation.SnapshotJson,N'$.paymentCorrections') WITH(
        SourceId uniqueidentifier N'$.sourceId',SourceNumber int N'$.sourceNumber',MovementType nvarchar(32) N'$.movementType',
        CorrectionId uniqueidentifier N'$.correctionId',PaymentMethodId uniqueidentifier N'$.paymentMethodId',
        TenderMethodCode nvarchar(32) N'$.tenderMethodCode',TenderCategory nvarchar(128) N'$.tenderCategory',
        PaymentMethodName nvarchar(160) N'$.paymentMethodName',Amount decimal(19,4) N'$.amount',CardFranchiseCode nvarchar(32) N'$.cardFranchiseCode',ApprovalNumber nvarchar(160) N'$.approvalNumber') corrected
      LEFT JOIN dbo.AccountingPostingJobs posted ON posted.SourceDocumentId=reconciliation.ReconciliationId
        AND posted.SourceDocumentType=N'WorkSessionClosureReconciliation' AND posted.TenantId=@TenantId AND posted.BusinessId=@BusinessId
      OUTER APPLY(SELECT COUNT_BIG(*) ProofCount,MAX(CONVERT(nvarchar(36),applied.AccountId)) AccountId,MAX(applied.Name) Name
        FROM OPENJSON(posted.AppliedPaymentMethodsJson) WITH(SourceLineId uniqueidentifier,AccountId uniqueidentifier,Name nvarchar(160)) applied
        WHERE applied.SourceLineId=corrected.CorrectionId) binding
      OUTER APPLY(SELECT applied.AccountId FROM OPENJSON(posted.ResolvedAccountMappingsJson)
        WITH(Category nvarchar(128),AccountId uniqueidentifier) applied WHERE applied.Category=corrected.TenderCategory) target
      WHERE sale.CollectionMode=N'Session' AND closure.WorkSessionId=sale.WorkSessionId
        AND corrected.MovementType=N'Sale' AND corrected.SourceId=sale.DocumentId AND corrected.SourceNumber=payment.PaymentNumber
    ) correction
    WHERE sale.DocumentId=@DocumentId AND sale.BusinessId=@BusinessId
);
