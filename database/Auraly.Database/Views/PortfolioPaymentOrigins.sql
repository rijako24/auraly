-- Read-only identity projection. Payments keep their own IDs; a grouped payment
-- obtains its source and number from its parent instead of allocating a receipt.
CREATE VIEW accounting.PortfolioPaymentOrigins
AS
SELECT payment.PaymentId,N'Payable' SubledgerKind,business.TenantId,payment.BusinessId,
  COALESCE(payment.ParentSourceDocumentId,payment.PaymentId) SourceDocumentId,
  COALESCE(payment.ParentSourceDocumentType,N'PayablePayment') SourceDocumentType,payment.SourceGroupId,
  CONVERT(nvarchar(100),COALESCE(payment.DocumentNumber,entry.EntryNumber,draft.Reference,
    CONVERT(nvarchar(36),payment.ParentSourceDocumentId))) DocumentNumber
FROM dbo.SupplierPayments payment
JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=payment.ParentSourceDocumentId
  AND entry.SourceDocumentType=payment.ParentSourceDocumentType AND entry.BusinessId=payment.BusinessId
  AND entry.TenantId=business.TenantId
LEFT JOIN accounting.VoucherDrafts draft ON draft.DocumentId=payment.ParentSourceDocumentId
  AND draft.DocumentType=payment.ParentSourceDocumentType AND draft.BusinessId=payment.BusinessId
  AND draft.TenantId=business.TenantId AND draft.SentAt IS NOT NULL
UNION ALL
SELECT payment.PaymentId,N'Receivable',business.TenantId,payment.BusinessId,
  COALESCE(payment.ParentSourceDocumentId,payment.PaymentId),
  COALESCE(payment.ParentSourceDocumentType,N'ReceivablePayment'),payment.SourceGroupId,
  CONVERT(nvarchar(100),COALESCE(payment.DocumentNumber,entry.EntryNumber,draft.Reference,
    CONVERT(nvarchar(36),payment.ParentSourceDocumentId)))
FROM dbo.CustomerPayments payment
JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=payment.ParentSourceDocumentId
  AND entry.SourceDocumentType=payment.ParentSourceDocumentType AND entry.BusinessId=payment.BusinessId
  AND entry.TenantId=business.TenantId
LEFT JOIN accounting.VoucherDrafts draft ON draft.DocumentId=payment.ParentSourceDocumentId
  AND draft.DocumentType=payment.ParentSourceDocumentType AND draft.BusinessId=payment.BusinessId
  AND draft.TenantId=business.TenantId AND draft.SentAt IS NOT NULL;
GO
