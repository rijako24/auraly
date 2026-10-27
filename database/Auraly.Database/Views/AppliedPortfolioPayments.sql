-- Evidence of an actually applied payment, shared by unapplication capture,
-- acceptance and posting. This projection has no independent state or writer.
CREATE VIEW accounting.AppliedPortfolioPayments
AS
SELECT N'Payable' SubledgerKind,original.PayableTransactionId TransactionId,position.PayableId PositionId,
  business.TenantId,position.BusinessId,party.PartyId,COALESCE(party.DisplayName,party.LegalName,party.Identification,CONVERT(nvarchar(36),party.PartyId)) PartyName,party.Identification PartyIdentification,
  position.SupplierId CounterpartyRoleId,position.PartySiteId,position.DocumentNumber,position.CurrencyCode,
  position.OpeningEntryId,position.OpeningEntryLineNumber,original.Amount,original.OccurredAt,payment.PaymentId,payment.WorkSessionId,position.Status PositionStatus
FROM dbo.PayableTransactions original
JOIN dbo.Payables position ON position.PayableId=original.PayableId
JOIN dbo.Businesses business ON business.BusinessId=position.BusinessId
JOIN dbo.Suppliers supplier ON supplier.SupplierId=position.SupplierId AND supplier.TenantId=business.TenantId
JOIN dbo.Parties party ON party.PartyId=supplier.PartyId AND party.TenantId=business.TenantId
JOIN dbo.SupplierPayments payment ON payment.PaymentId=original.SourceDocumentId AND payment.BusinessId=position.BusinessId
  AND payment.SupplierId=position.SupplierId AND payment.CurrencyCode=position.CurrencyCode AND payment.Status=N'Processed'
JOIN dbo.SupplierPaymentApplications application ON application.PaymentId=payment.PaymentId AND application.PayableId=position.PayableId
  AND application.Amount=original.Amount AND application.AppliedAt IS NOT NULL
WHERE original.TransactionType=N'Payment' AND original.Amount>0
UNION ALL
SELECT N'Receivable',original.ReceivableTransactionId,position.ReceivableId,
  business.TenantId,position.BusinessId,party.PartyId,COALESCE(party.DisplayName,party.LegalName,party.Identification,CONVERT(nvarchar(36),party.PartyId)),party.Identification,
  position.CustomerId,position.PartySiteId,position.DocumentNumber,position.CurrencyCode,
  position.OpeningEntryId,position.OpeningEntryLineNumber,original.Amount,original.OccurredAt,payment.PaymentId,payment.WorkSessionId,position.Status
FROM dbo.ReceivableTransactions original
JOIN dbo.Receivables position ON position.ReceivableId=original.ReceivableId
JOIN dbo.Businesses business ON business.BusinessId=position.BusinessId
LEFT JOIN dbo.Customers customer ON customer.CustomerId=position.CustomerId AND customer.TenantId=business.TenantId
JOIN dbo.Parties party ON party.PartyId=COALESCE(position.PartyId,customer.PartyId) AND party.TenantId=business.TenantId
JOIN dbo.CustomerPayments payment ON payment.PaymentId=original.SourceDocumentId AND payment.BusinessId=position.BusinessId
  AND payment.CurrencyCode=position.CurrencyCode AND payment.Status=N'Processed'
  AND (payment.CustomerId=position.CustomerId AND payment.PartyId IS NULL
    OR payment.CustomerId IS NULL AND position.CustomerId IS NULL AND payment.PartyId=position.PartyId)
JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=payment.PaymentId AND application.ReceivableId=position.ReceivableId
  AND application.Amount=original.Amount AND application.AppliedAt IS NOT NULL
WHERE original.TransactionType=N'Payment' AND original.Amount>0;
GO
