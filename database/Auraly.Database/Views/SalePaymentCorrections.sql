CREATE VIEW accounting.SalePaymentCorrections
AS
SELECT source.TenantId,source.BusinessId,source.SourceDocumentId,
  source.CorrectsSaleDocumentId DocumentId,source.PaymentCorrectionClosureId WorkSessionClosureId,
  source.PaymentCorrectionVerificationKey VerificationKey,source.AcceptedAt,
  job.Status PostingStatus,job.AccountingEntryRequired,correction.CorrectionId,
  correction.MovementType,correction.SourceNumber,correction.OriginalPaymentMethodCode,
  correction.OriginalAmount,correction.PaymentMethodCode,correction.Amount,
  correction.TenderMethodCode,correction.CardFranchiseCode,correction.ApprovalNumber,
  correction.Reference,correction.Reason,correction.PaymentMethodId,correction.PaymentMethodVersion,
  COALESCE(binding.Name,correction.PaymentMethodName) PaymentMethodName,
  CASE WHEN binding.ProofCount=1 THEN TRY_CONVERT(uniqueidentifier,binding.AccountId) END AccountId,
  JSON_QUERY(source.PayloadJson,N'$.closureAllocations') ClosureAllocationsJson
FROM dbo.AccountingSourceDocuments source
JOIN dbo.AccountingPostingJobs job ON job.AccountingPostingJobId=source.AccountingPostingJobId
  AND job.TenantId=source.TenantId AND job.BusinessId=source.BusinessId
  AND job.SourceDocumentId=source.SourceDocumentId AND job.SourceDocumentType=source.SourceDocumentType
CROSS APPLY OPENJSON(source.PayloadJson,N'$.paymentCorrections') WITH(
  CorrectionId uniqueidentifier '$.correctionId',MovementType nvarchar(32) '$.movementType',
  SourceNumber int '$.sourceNumber',OriginalPaymentMethodCode nvarchar(32) '$.originalPaymentMethodCode',
  OriginalAmount decimal(19,4) '$.originalAmount',PaymentMethodCode nvarchar(32) '$.paymentMethodCode',
  Amount decimal(19,4) '$.amount',TenderMethodCode nvarchar(32) '$.tenderMethodCode',
  CardFranchiseCode nvarchar(64) '$.cardFranchiseCode',ApprovalNumber nvarchar(100) '$.approvalNumber',
  Reference nvarchar(160) '$.reference',Reason nvarchar(500) '$.reason',
  PaymentMethodId uniqueidentifier '$.paymentMethodId',PaymentMethodVersion int '$.paymentMethodVersion',
  PaymentMethodName nvarchar(160) '$.paymentMethodName') correction
OUTER APPLY(SELECT COUNT_BIG(*) ProofCount,MAX(CONVERT(nvarchar(36),applied.AccountId)) AccountId,MAX(applied.Name) Name
  FROM OPENJSON(job.AppliedPaymentMethodsJson) WITH(SourceLineId uniqueidentifier,AccountId uniqueidentifier,Name nvarchar(160)) applied
  WHERE applied.SourceLineId=correction.CorrectionId) binding
WHERE source.CorrectsSaleDocumentId IS NOT NULL
  AND source.SourceDocumentType=N'WorkSessionClosureReconciliation';
GO
