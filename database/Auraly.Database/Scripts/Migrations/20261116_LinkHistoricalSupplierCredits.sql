-- Link unchanged, already posted expense-cancellation credits to their original
-- journal. No source is replayed, no account is selected and no balance changes.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

SELECT credit.SupplierCreditId,credit.BusinessId,business.TenantId,credit.SupplierId,
    supplier.PartyId,credit.SourceDocumentId,credit.OriginalAmount,credit.AvailableAmount,
    credit.Status,credit.CreatedAt,credit.CurrencyCode,credit.CreditNature,
    credit.OpeningEntryId,credit.OpeningEntryLineNumber,
    job.SourcePayloadHash,job.OccurredAt SourceOccurredAt,
    source.PayloadHash,source.OccurredAt CapturedOccurredAt,source.AccountingEntryRequired,
    entry.EntryId,entry.SourcePayloadHash EntryPayloadHash,entry.OccurredAt EntryOccurredAt,
    expense.ExpenseId,expense.SupplierId ExpenseSupplierId,expense.Status ExpenseStatus,
    expense.NetPayable,expense.CurrencyCode ExpenseCurrency,
    TRY_CONVERT(uniqueidentifier,JSON_VALUE(source.PayloadJson,'$.tenantId')) PayloadTenant,
    TRY_CONVERT(uniqueidentifier,JSON_VALUE(source.PayloadJson,'$.businessId')) PayloadBusiness,
    TRY_CONVERT(uniqueidentifier,JSON_VALUE(source.PayloadJson,'$.cancellationId')) CancellationId,
    TRY_CONVERT(datetimeoffset(7),JSON_VALUE(source.PayloadJson,'$.cancelledAt')) CancelledAt,
    TRY_CONVERT(uniqueidentifier,JSON_VALUE(source.PayloadJson,'$.original.tenantId')) OriginalTenant,
    TRY_CONVERT(uniqueidentifier,JSON_VALUE(source.PayloadJson,'$.original.businessId')) OriginalBusiness,
    TRY_CONVERT(uniqueidentifier,JSON_VALUE(source.PayloadJson,'$.original.expenseId')) OriginalExpenseId,
    TRY_CONVERT(uniqueidentifier,JSON_VALUE(source.PayloadJson,'$.original.supplierId')) OriginalSupplierId,
    JSON_VALUE(source.PayloadJson,'$.original.currencyCode') ProvenCurrency,
    TRY_CONVERT(decimal(19,4),JSON_VALUE(source.PayloadJson,'$.original.withholding.netAmount')) OriginalNet,
    TRY_CONVERT(decimal(19,4),JSON_VALUE(source.PayloadJson,'$.supplierCredit')) ProvenCredit,
    TRY_CONVERT(decimal(19,4),JSON_VALUE(source.PayloadJson,'$.payableCredit')) ProvenPayableCredit
INTO #HistoricalSupplierCredits
FROM dbo.SupplierCredits credit WITH(UPDLOCK,HOLDLOCK)
JOIN dbo.Businesses business ON business.BusinessId=credit.BusinessId
JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=credit.SourceDocumentId
    AND job.SourceDocumentType=N'ExpenseCancellation' AND job.BusinessId=credit.BusinessId
    AND job.TenantId=business.TenantId AND job.Status=N'Posted' AND job.AccountingEntryRequired=1
LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId AND supplier.TenantId=business.TenantId
LEFT JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=job.SourceDocumentId
    AND source.SourceDocumentType=job.SourceDocumentType AND source.BusinessId=credit.BusinessId AND source.TenantId=business.TenantId
LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
    AND entry.SourceDocumentType=job.SourceDocumentType AND entry.BusinessId=credit.BusinessId AND entry.TenantId=business.TenantId
LEFT JOIN dbo.Expenses expense ON expense.CancellationId=credit.SourceDocumentId AND expense.BusinessId=credit.BusinessId
WHERE credit.SourceDocumentType=N'ExpenseCancellation' AND NOT EXISTS(
    SELECT 1 FROM dbo.SupplierCreditTransactions history WITH(UPDLOCK,HOLDLOCK)
    WHERE history.SupplierCreditId=credit.SupplierCreditId AND history.TransactionType=N'Opening');

-- Commercial-only and pending sources are outside this migration. A posted
-- candidate must prove every identity and amount; missing evidence is not zero.
IF EXISTS(SELECT 1 FROM #HistoricalSupplierCredits credit
    WHERE credit.PartyId IS NULL OR credit.PayloadHash IS NULL OR credit.EntryId IS NULL
      OR credit.SourcePayloadHash<>credit.PayloadHash OR credit.SourcePayloadHash<>credit.EntryPayloadHash
      OR credit.AccountingEntryRequired=0 OR credit.CapturedOccurredAt<>credit.SourceOccurredAt
      OR credit.EntryOccurredAt<>credit.SourceOccurredAt
      OR credit.PayloadTenant IS NULL OR credit.PayloadTenant<>credit.TenantId
      OR credit.PayloadBusiness IS NULL OR credit.PayloadBusiness<>credit.BusinessId
      OR credit.CancellationId IS NULL OR credit.CancellationId<>credit.SourceDocumentId
      OR credit.CancelledAt IS NULL OR credit.CancelledAt<>credit.SourceOccurredAt
      OR credit.OriginalTenant IS NULL OR credit.OriginalTenant<>credit.TenantId
      OR credit.OriginalBusiness IS NULL OR credit.OriginalBusiness<>credit.BusinessId
      OR credit.ExpenseId IS NULL OR credit.ExpenseStatus<>N'Cancelled'
      OR credit.ExpenseSupplierId<>credit.SupplierId
      OR credit.OriginalExpenseId IS NULL OR credit.OriginalExpenseId<>credit.ExpenseId
      OR credit.OriginalSupplierId IS NULL OR credit.OriginalSupplierId<>credit.SupplierId
      OR credit.ProvenCurrency IS NULL OR credit.ProvenCurrency<>N'COP' OR credit.ProvenCurrency<>credit.ExpenseCurrency
      OR (credit.CurrencyCode IS NOT NULL AND credit.CurrencyCode<>credit.ProvenCurrency)
      OR (credit.CreditNature IS NOT NULL AND credit.CreditNature<>N'Credit')
      OR credit.OriginalNet IS NULL OR credit.OriginalNet<>credit.NetPayable
      OR credit.ProvenCredit IS NULL OR credit.ProvenCredit<>credit.OriginalAmount
      OR credit.ProvenPayableCredit IS NULL OR credit.ProvenPayableCredit<0
      OR credit.ProvenPayableCredit+credit.ProvenCredit<>credit.OriginalNet
      OR credit.Status<>N'Open' OR credit.OriginalAmount<>credit.AvailableAmount
      OR EXISTS(SELECT 1 FROM dbo.SupplierCreditTransactions history WHERE history.SupplierCreditId=credit.SupplierCreditId))
    THROW 51985,N'Un saldo histórico de anulación de gasto no tiene evidencia suficiente para enlazar su apertura. No se modificaron saldos ni asientos.',1;

SELECT credit.SupplierCreditId,line.EntryId,line.LineNumber
INTO #HistoricalSupplierCreditOpenings
FROM #HistoricalSupplierCredits credit
JOIN dbo.AccountingEntryLines line ON line.EntryId=credit.EntryId
    AND line.PartyId=credit.PartyId AND line.Debit=credit.OriginalAmount AND line.Credit=0
CROSS APPLY accounting.ObligationOpeningAccount(credit.TenantId,credit.BusinessId,N'SupplierCredit',
    line.EntryId,line.LineNumber,credit.PartyId) opening
WHERE opening.Issue IS NULL;

IF EXISTS(SELECT 1 FROM #HistoricalSupplierCredits credit
    LEFT JOIN #HistoricalSupplierCreditOpenings opening ON opening.SupplierCreditId=credit.SupplierCreditId
    GROUP BY credit.SupplierCreditId HAVING COUNT(opening.LineNumber)<>1)
    OR EXISTS(SELECT 1 FROM #HistoricalSupplierCredits credit
      JOIN #HistoricalSupplierCreditOpenings opening ON opening.SupplierCreditId=credit.SupplierCreditId
      WHERE (credit.OpeningEntryId IS NOT NULL AND credit.OpeningEntryId<>opening.EntryId)
        OR (credit.OpeningEntryLineNumber IS NOT NULL AND credit.OpeningEntryLineNumber<>opening.LineNumber))
    OR EXISTS(SELECT 1 FROM #HistoricalSupplierCreditOpenings opening
      JOIN dbo.SupplierCredits other ON other.OpeningEntryId=opening.EntryId AND other.OpeningEntryLineNumber=opening.LineNumber
        AND other.SupplierCreditId<>opening.SupplierCreditId)
    THROW 51985,N'El saldo histórico del proveedor no tiene una única partida contable conciliable. No se modificó la cartera.',1;

UPDATE credit SET CurrencyCode=proven.ProvenCurrency,CreditNature=N'Credit',
    OpeningEntryId=opening.EntryId,OpeningEntryLineNumber=opening.LineNumber
FROM dbo.SupplierCredits credit JOIN #HistoricalSupplierCredits proven ON proven.SupplierCreditId=credit.SupplierCreditId
JOIN #HistoricalSupplierCreditOpenings opening ON opening.SupplierCreditId=credit.SupplierCreditId;

INSERT dbo.SupplierCreditTransactions(SupplierCreditTransactionId,SupplierCreditId,TransactionType,Amount,
    SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
SELECT NEWID(),credit.SupplierCreditId,N'Opening',credit.OriginalAmount,
    credit.SourceDocumentId,N'ExpenseCancellation',credit.SourceOccurredAt,credit.CreatedAt
FROM #HistoricalSupplierCredits credit;

DROP TABLE #HistoricalSupplierCreditOpenings;
DROP TABLE #HistoricalSupplierCredits;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
