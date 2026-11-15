-- Complete provenance for unchanged historical return credits, using their
-- already posted source. Never replay a return, change a balance or post again.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

SELECT credit.CustomerCreditId,credit.BusinessId,business.TenantId,credit.CustomerId,
    customer.PartyId,credit.SourceReturnId,credit.OriginalAmount,credit.AvailableAmount,
    credit.Status,credit.CreatedAt,credit.CurrencyCode,credit.CreditNature,
    credit.OpeningEntryId,credit.OpeningEntryLineNumber,
    returned.ReturnId,returned.Status ReturnStatus,returned.EconomicResolution,
    returned.CustomerId ReturnCustomerId,returned.ReturnedAt,
    returned.TotalAmount-COALESCE(applied.Amount,0) ProvenAmount,
    job.AccountingPostingJobId,job.Status PostingStatus,job.AccountingEntryRequired,
    job.SourcePayloadHash,job.OccurredAt SourceOccurredAt,
    entry.EntryId,entry.SourcePayloadHash EntryPayloadHash,entry.OccurredAt EntryOccurredAt,
    CASE WHEN original.DocumentType=N'SalesReceipt' THEN N'COP'
         ELSE JSON_VALUE(snapshot.SnapshotJson,'$.ublSnapshot.currencyCode') END ProvenCurrency
INTO #HistoricalCustomerCredits
FROM dbo.CustomerCredits credit WITH(UPDLOCK,HOLDLOCK)
JOIN dbo.Businesses business ON business.BusinessId=credit.BusinessId
LEFT JOIN dbo.Customers customer ON customer.CustomerId=credit.CustomerId AND customer.TenantId=business.TenantId
LEFT JOIN dbo.SalesReturns returned ON returned.ReturnId=credit.SourceReturnId AND returned.BusinessId=credit.BusinessId
LEFT JOIN dbo.SalesDocuments original ON original.DocumentId=returned.OriginalDocumentId
    AND original.BusinessId=credit.BusinessId AND original.CustomerId=credit.CustomerId
LEFT JOIN dbo.FiscalSnapshots snapshot ON snapshot.DocumentId=original.DocumentId
LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=returned.ReturnId
    AND job.SourceDocumentType=N'SalesReturn' AND job.BusinessId=credit.BusinessId AND job.TenantId=business.TenantId
LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
    AND entry.SourceDocumentType=job.SourceDocumentType AND entry.BusinessId=credit.BusinessId AND entry.TenantId=business.TenantId
OUTER APPLY(SELECT SUM(application.Amount) Amount FROM dbo.SalesReturnReceivableApplications application
    WHERE application.ReturnId=returned.ReturnId) applied
WHERE credit.SourceReturnId IS NOT NULL AND NOT EXISTS(
    SELECT 1 FROM dbo.CustomerCreditTransactions history WITH(UPDLOCK,HOLDLOCK)
    WHERE history.CustomerCreditId=credit.CustomerCreditId AND history.TransactionType=N'Opening');

-- Sales receipts have a COP-only contract. Electronic invoices instead prove
-- their currency through the original immutable fiscal snapshot.
IF EXISTS(SELECT 1 FROM #HistoricalCustomerCredits credit
    WHERE credit.PartyId IS NULL OR credit.ReturnId IS NULL OR credit.ReturnStatus<>N'Processed'
      OR credit.EconomicResolution<>N'CustomerCredit' OR credit.ReturnCustomerId<>credit.CustomerId
      OR credit.AccountingPostingJobId IS NULL OR credit.PostingStatus<>N'Posted'
      OR credit.AccountingEntryRequired<>1 OR credit.EntryId IS NULL
      OR credit.SourcePayloadHash<>credit.EntryPayloadHash
      OR credit.SourceOccurredAt<>credit.EntryOccurredAt OR credit.SourceOccurredAt<>credit.ReturnedAt
      OR credit.ProvenCurrency IS NULL OR credit.ProvenCurrency<>N'COP'
      OR (credit.CurrencyCode IS NOT NULL AND credit.CurrencyCode<>credit.ProvenCurrency)
      OR (credit.CreditNature IS NOT NULL AND credit.CreditNature<>N'Credit')
      OR credit.Status<>N'Open' OR credit.OriginalAmount<>credit.AvailableAmount
      OR credit.ProvenAmount<>credit.OriginalAmount
      OR EXISTS(SELECT 1 FROM dbo.CustomerCreditTransactions history WHERE history.CustomerCreditId=credit.CustomerCreditId))
    THROW 51872,N'Un saldo histórico de devolución no tiene evidencia suficiente para enlazar su apertura. Revisa su origen; no se modificaron saldos ni documentos.',1;

SELECT credit.CustomerCreditId,line.EntryId,line.LineNumber
INTO #HistoricalCreditOpenings
FROM #HistoricalCustomerCredits credit
JOIN dbo.AccountingEntryLines line ON line.EntryId=credit.EntryId
    AND line.PartyId=credit.PartyId AND line.Credit=credit.OriginalAmount AND line.Debit=0
CROSS APPLY accounting.ObligationOpeningAccount(credit.TenantId,credit.BusinessId,N'CustomerCredit',
    line.EntryId,line.LineNumber,credit.PartyId) opening
WHERE opening.Issue IS NULL;

IF EXISTS(SELECT 1 FROM #HistoricalCustomerCredits credit
    LEFT JOIN #HistoricalCreditOpenings opening ON opening.CustomerCreditId=credit.CustomerCreditId
    GROUP BY credit.CustomerCreditId HAVING COUNT(opening.LineNumber)<>1)
    OR EXISTS(SELECT 1 FROM #HistoricalCustomerCredits credit
      JOIN #HistoricalCreditOpenings opening ON opening.CustomerCreditId=credit.CustomerCreditId
      WHERE (credit.OpeningEntryId IS NOT NULL AND credit.OpeningEntryId<>opening.EntryId)
        OR (credit.OpeningEntryLineNumber IS NOT NULL AND credit.OpeningEntryLineNumber<>opening.LineNumber))
    OR EXISTS(SELECT 1 FROM #HistoricalCreditOpenings opening
      JOIN dbo.CustomerCredits other ON other.OpeningEntryId=opening.EntryId AND other.OpeningEntryLineNumber=opening.LineNumber
        AND other.CustomerCreditId<>opening.CustomerCreditId)
    THROW 51872,N'La apertura del saldo histórico no tiene una única partida contable conciliable. No se modificó la cartera.',1;

UPDATE credit SET CurrencyCode=proven.ProvenCurrency,CreditNature=N'Credit',
    OpeningEntryId=opening.EntryId,OpeningEntryLineNumber=opening.LineNumber
FROM dbo.CustomerCredits credit JOIN #HistoricalCustomerCredits proven ON proven.CustomerCreditId=credit.CustomerCreditId
JOIN #HistoricalCreditOpenings opening ON opening.CustomerCreditId=credit.CustomerCreditId;

INSERT dbo.CustomerCreditTransactions(CustomerCreditTransactionId,CustomerCreditId,TransactionType,Amount,
    SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
SELECT NEWID(),credit.CustomerCreditId,N'Opening',credit.OriginalAmount,
    credit.SourceReturnId,N'SalesReturn',credit.SourceOccurredAt,credit.CreatedAt
FROM #HistoricalCustomerCredits credit;

DROP TABLE #HistoricalCreditOpenings;
DROP TABLE #HistoricalCustomerCredits;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
