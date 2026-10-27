-- Original facts for the shared unapplication path. Payment evidence retains its
-- existing owner; a non-cash application is proven by its paired credit history.
-- This is a read projection, never another balance or writer.
CREATE VIEW accounting.AppliedPortfolioSettlements
AS
SELECT payment.SubledgerKind,payment.TransactionId,payment.PositionId,payment.TenantId,payment.BusinessId,
  payment.PartyId,payment.PartyName,payment.PartyIdentification,payment.CounterpartyRoleId,payment.PartySiteId,
  payment.DocumentNumber,payment.CurrencyCode,payment.OpeningEntryId,payment.OpeningEntryLineNumber,
  payment.Amount,payment.OccurredAt,payment.PaymentId,payment.WorkSessionId,payment.PositionStatus,
  CONVERT(nvarchar(24),N'Payment') TransactionType,
  CAST(NULL AS uniqueidentifier) CreditId,CAST(NULL AS uniqueidentifier) CreditTransactionId,
  CAST(NULL AS uniqueidentifier) CreditOpeningEntryId,CAST(NULL AS int) CreditOpeningEntryLineNumber,
  CAST(NULL AS uniqueidentifier) CreditCustomerId
FROM accounting.AppliedPortfolioPayments payment
UNION ALL
SELECT N'Payable',original.PayableTransactionId,position.PayableId,
  business.TenantId,position.BusinessId,party.PartyId,COALESCE(party.DisplayName,party.LegalName,party.Identification,CONVERT(nvarchar(36),party.PartyId)),party.Identification,
  position.SupplierId,position.PartySiteId,position.DocumentNumber,position.CurrencyCode,
  position.OpeningEntryId,position.OpeningEntryLineNumber,original.Amount,original.OccurredAt,
  CAST(NULL AS uniqueidentifier),CAST(NULL AS uniqueidentifier),position.Status,N'CreditApplication',
  credit.SupplierCreditId,application.SupplierCreditTransactionId,credit.OpeningEntryId,credit.OpeningEntryLineNumber,
  CAST(NULL AS uniqueidentifier)
FROM dbo.PayableTransactions original
JOIN dbo.Payables position ON position.PayableId=original.PayableId
JOIN dbo.Businesses business ON business.BusinessId=position.BusinessId
JOIN dbo.Suppliers supplier ON supplier.SupplierId=position.SupplierId AND supplier.TenantId=business.TenantId
JOIN dbo.Parties party ON party.PartyId=supplier.PartyId AND party.TenantId=business.TenantId
JOIN dbo.SupplierCreditTransactions application ON application.AppliedPositionTransactionId=original.PayableTransactionId
  AND application.TransactionType=N'Application' AND application.Amount=original.Amount
  AND application.SourceDocumentId=original.SourceDocumentId AND application.SourceDocumentType=original.SourceDocumentType
JOIN dbo.SupplierCredits credit ON credit.SupplierCreditId=application.SupplierCreditId
  AND credit.BusinessId=position.BusinessId AND credit.SupplierId=position.SupplierId
  AND credit.CurrencyCode=position.CurrencyCode AND credit.CreditNature IN(N'Credit',N'Advance')
  AND credit.Status<>N'Cancelled'
  AND (credit.PartySiteId=position.PartySiteId OR credit.PartySiteId IS NULL AND position.PartySiteId IS NULL)
WHERE original.TransactionType=N'CreditApplication' AND original.Amount>0
UNION ALL
SELECT N'Receivable',original.ReceivableTransactionId,position.ReceivableId,
  business.TenantId,position.BusinessId,party.PartyId,COALESCE(party.DisplayName,party.LegalName,party.Identification,CONVERT(nvarchar(36),party.PartyId)),party.Identification,
  position.CustomerId,position.PartySiteId,position.DocumentNumber,position.CurrencyCode,
  position.OpeningEntryId,position.OpeningEntryLineNumber,original.Amount,original.OccurredAt,
  CAST(NULL AS uniqueidentifier),CAST(NULL AS uniqueidentifier),position.Status,N'CreditApplication',
  credit.CustomerCreditId,application.CustomerCreditTransactionId,credit.OpeningEntryId,credit.OpeningEntryLineNumber,
  credit.CustomerId
FROM dbo.ReceivableTransactions original
JOIN dbo.Receivables position ON position.ReceivableId=original.ReceivableId
JOIN dbo.Businesses business ON business.BusinessId=position.BusinessId
LEFT JOIN dbo.Customers customer ON customer.CustomerId=position.CustomerId AND customer.TenantId=business.TenantId
JOIN dbo.Parties party ON party.PartyId=COALESCE(position.PartyId,customer.PartyId) AND party.TenantId=business.TenantId
JOIN dbo.CustomerCreditTransactions application ON application.AppliedPositionTransactionId=original.ReceivableTransactionId
  AND application.TransactionType=N'Application' AND application.Amount=original.Amount
  AND application.SourceDocumentId=original.SourceDocumentId AND application.SourceDocumentType=original.SourceDocumentType
JOIN dbo.CustomerCredits credit ON credit.CustomerCreditId=application.CustomerCreditId
  AND credit.BusinessId=position.BusinessId AND credit.CurrencyCode=position.CurrencyCode
  AND credit.CreditNature IN(N'Credit',N'Advance')
LEFT JOIN dbo.Customers creditCustomer ON creditCustomer.CustomerId=credit.CustomerId AND creditCustomer.TenantId=business.TenantId
WHERE original.TransactionType=N'CreditApplication' AND original.Amount>0
  AND COALESCE(credit.PartyId,creditCustomer.PartyId)=party.PartyId;
GO
