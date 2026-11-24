CREATE FUNCTION dispatch.EffectivePayments(@DispatchId UNIQUEIDENTIFIER)
RETURNS TABLE
AS RETURN
(
    WITH Applied AS
    (
      SELECT binding.SourceLineId PaymentId,binding.MethodCode,binding.PaymentMethodId,binding.Version,
        binding.Name,binding.BankAccountId
      FROM dbo.WorkSessions session
      JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
      JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=closure.WorkSessionClosureId
        AND job.SourceDocumentType=N'DispatchCashDifference' AND job.TenantId=session.TenantId
        AND job.BusinessId=session.BusinessId AND job.Status IN(N'Posted',N'CommercialEffectsApplied')
      CROSS APPLY OPENJSON(job.AppliedPaymentMethodsJson) WITH(SourceLineId uniqueidentifier,MethodCode nvarchar(32),
        PaymentMethodId uniqueidentifier,Version int,Name nvarchar(160),BankAccountId uniqueidentifier) binding
      WHERE session.DispatchId=@DispatchId AND session.Kind=N'Dispatch'
      UNION ALL
      -- Credit collections are posted by the native CxC payment, not again by
      -- the dispatch journal. Read its applied tender after the parent finishes;
      -- later catalog edits must not rename or remap a completed collection.
      SELECT DISTINCT recorded.DispatchDeliveryPaymentId,tender.MethodCode,tender.PaymentMethodId,
        tender.PaymentMethodVersion,tender.PaymentMethodName,tender.BankAccountId
      FROM dbo.DispatchDeliveryPayments recorded
      JOIN dbo.DispatchSourceDocuments document ON document.DispatchSourceDocumentId=recorded.DispatchSourceDocumentId
        AND document.DispatchId=recorded.DispatchId
      JOIN dbo.CustomerPayments native ON native.BusinessId=recorded.BusinessId AND native.Status=N'Processed'
        AND native.IdempotencyKey=CONCAT(N'dispatch-settlement:',LOWER(REPLACE(CONVERT(nvarchar(36),recorded.DispatchId),N'-',N'')),
          N':payment:',LOWER(REPLACE(CONVERT(nvarchar(36),document.SourceDocumentId),N'-',N'')),N':',recorded.PaymentMethod)
      JOIN dbo.CustomerPaymentTenders tender ON tender.PaymentId=native.PaymentId
        AND tender.PaymentMethodId=COALESCE(recorded.ReviewedPaymentMethodId,recorded.PaymentMethodId)
      JOIN dbo.WorkSessions session ON session.DispatchId=recorded.DispatchId AND session.Kind=N'Dispatch'
      JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
      JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=closure.WorkSessionClosureId
        AND financial.SourceDocumentType=N'DispatchCashDifference' AND financial.TenantId=session.TenantId
        AND financial.BusinessId=session.BusinessId AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')
      WHERE recorded.DispatchId=@DispatchId AND recorded.ApplicationType=N'CreditAdvance'
    )
    SELECT payment.DispatchDeliveryPaymentId PaymentId,payment.DispatchId,payment.BusinessId,
      payment.DispatchSourceDocumentId,payment.ApplicationType,payment.Amount,payment.PaymentMethod OriginalMethod,
      payment.PaymentMethodId OriginalPaymentMethodId,originalForm.Name OriginalFormName,
      CASE WHEN applied.PaymentId IS NOT NULL THEN applied.MethodCode WHEN payment.ReviewedPaymentMethodId IS NOT NULL THEN reviewedForm.MethodCode
        WHEN payment.PaymentMethod=N'Deposit' THEN N'Transfer' ELSE payment.PaymentMethod END MethodCode,
      COALESCE(applied.PaymentMethodId,payment.ReviewedPaymentMethodId,payment.PaymentMethodId) PaymentMethodId,
      COALESCE(applied.Version,payment.ReviewedPaymentMethodVersion,payment.PaymentMethodVersion) PaymentMethodVersion,
      COALESCE(applied.Name,reviewedForm.Name,originalForm.Name) FormName,
      CASE WHEN applied.PaymentId IS NOT NULL THEN applied.BankAccountId WHEN payment.ReviewedPaymentMethodId IS NOT NULL THEN reviewedForm.BankAccountId ELSE originalForm.BankAccountId END BankAccountId,
      CASE WHEN payment.ReviewedPaymentMethodId IS NOT NULL THEN payment.ReviewedReference ELSE payment.Reference END Reference,
      CASE WHEN payment.ReviewedPaymentMethodId IS NOT NULL THEN payment.ReviewedCardFranchiseCode ELSE payment.CardFranchiseCode END CardFranchiseCode,
      CASE WHEN payment.ReviewedPaymentMethodId IS NOT NULL THEN payment.ReviewedApprovalNumber ELSE payment.ApprovalNumber END ApprovalNumber,
      payment.EvidenceUrl,payment.CorrectionReason,payment.CorrectedBy,payment.CorrectedAt,
      payment.VerifiedBy,payment.VerifiedAt
    FROM dbo.DispatchDeliveryPayments payment
    INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=payment.DispatchId AND dispatch.BusinessId=payment.BusinessId
    LEFT JOIN reference.PaymentMethodDetails originalForm ON originalForm.PaymentMethodId=payment.PaymentMethodId AND originalForm.TenantId=dispatch.TenantId
    LEFT JOIN reference.PaymentMethodDetails reviewedForm ON reviewedForm.PaymentMethodId=payment.ReviewedPaymentMethodId AND reviewedForm.TenantId=dispatch.TenantId
    LEFT JOIN Applied applied ON applied.PaymentId=payment.DispatchDeliveryPaymentId
    WHERE payment.DispatchId=@DispatchId
);
