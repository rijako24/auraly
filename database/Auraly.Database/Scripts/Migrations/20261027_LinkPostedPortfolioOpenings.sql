-- A posted historical obligation already has an immutable journal line. Link
-- that line before strict original-account readers are deployed. Never infer an
-- account from today's mappings or create another obligation, entry or payment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

CREATE TABLE #HistoricalObligations
(
  Kind nvarchar(16) COLLATE DATABASE_DEFAULT NOT NULL,
  PositionId uniqueidentifier NOT NULL,
  TenantId uniqueidentifier NOT NULL,
  BusinessId uniqueidentifier NOT NULL,
  PartyId uniqueidentifier NULL,
  SourceDocumentId uniqueidentifier NOT NULL,
  SourceDocumentType nvarchar(64) COLLATE DATABASE_DEFAULT NOT NULL,
  SourceLineId uniqueidentifier NULL,
  OriginalAmount decimal(19,4) NOT NULL,
  PRIMARY KEY(Kind,PositionId)
);

INSERT #HistoricalObligations
SELECT N'Receivable',balance.ReceivableId,job.TenantId,balance.BusinessId,
  COALESCE(balance.PartyId,customer.PartyId),balance.SourceDocumentId,
  balance.SourceDocumentType,balance.SourceLineId,balance.OriginalAmount
FROM dbo.Receivables balance
JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=balance.SourceDocumentId
  AND job.SourceDocumentType=balance.SourceDocumentType AND job.BusinessId=balance.BusinessId
JOIN dbo.Businesses business ON business.BusinessId=balance.BusinessId AND business.TenantId=job.TenantId
LEFT JOIN dbo.Customers customer ON customer.CustomerId=balance.CustomerId AND customer.TenantId=job.TenantId
WHERE job.AccountingEntryRequired=1 AND job.Status=N'Posted' AND balance.OpeningEntryId IS NULL
  AND balance.OpeningEntryLineNumber IS NULL;

INSERT #HistoricalObligations
SELECT N'Payable',balance.PayableId,job.TenantId,balance.BusinessId,
  supplier.PartyId,balance.SourceDocumentId,balance.SourceDocumentType,
  balance.SourceLineId,balance.OriginalAmount
FROM dbo.Payables balance
JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=balance.SourceDocumentId
  AND job.SourceDocumentType=balance.SourceDocumentType AND job.BusinessId=balance.BusinessId
JOIN dbo.Businesses business ON business.BusinessId=balance.BusinessId AND business.TenantId=job.TenantId
LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=balance.SupplierId AND supplier.TenantId=job.TenantId
WHERE job.AccountingEntryRequired=1 AND job.Status=N'Posted' AND balance.OpeningEntryId IS NULL
  AND balance.OpeningEntryLineNumber IS NULL;

CREATE TABLE #OpeningCandidates
(
  Kind nvarchar(16) COLLATE DATABASE_DEFAULT NOT NULL,
  PositionId uniqueidentifier NOT NULL,
  EntryId uniqueidentifier NOT NULL,
  LineNumber int NOT NULL,
  PRIMARY KEY(Kind,PositionId,EntryId,LineNumber)
);
INSERT #OpeningCandidates
SELECT position.Kind,position.PositionId,entry.EntryId,line.LineNumber
FROM #HistoricalObligations position
JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=position.SourceDocumentId
  AND job.SourceDocumentType=position.SourceDocumentType
  AND job.TenantId=position.TenantId AND job.BusinessId=position.BusinessId
JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=position.SourceDocumentId
  AND entry.SourceDocumentType=position.SourceDocumentType
  AND entry.TenantId=position.TenantId AND entry.BusinessId=position.BusinessId
  AND entry.SourcePayloadHash=job.SourcePayloadHash
JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.PartyId=position.PartyId
  AND (position.SourceLineId IS NULL OR line.SourceLineId=position.SourceLineId)
JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=position.TenantId
  AND account.IsActive=1 AND account.AllowsPosting=1
  AND (account.BusinessId IS NULL OR account.BusinessId=position.BusinessId)
  AND (account.FinancialFunction IS NULL OR account.FinancialFunction=position.Kind)
WHERE position.PartyId IS NOT NULL AND
  ((position.Kind=N'Receivable' AND account.AccountType=N'Asset'
      AND line.Debit=position.OriginalAmount AND line.Credit=0)
   OR (position.Kind=N'Payable' AND account.AccountType=N'Liability'
      AND line.Credit=position.OriginalAmount AND line.Debit=0));

IF EXISTS(SELECT 1 FROM #HistoricalObligations position
  LEFT JOIN #OpeningCandidates candidate ON candidate.Kind=position.Kind AND candidate.PositionId=position.PositionId
  GROUP BY position.Kind,position.PositionId HAVING COUNT(candidate.EntryId)<>1)
  THROW 51871,N'Hay obligaciones contabilizadas sin una partida de apertura original única y válida. Concilia sus asientos antes del corte.',1;
IF EXISTS(SELECT 1 FROM #OpeningCandidates GROUP BY EntryId,LineNumber HAVING COUNT(*)>1)
  THROW 51871,N'Una partida contable histórica abre más de una obligación. Concilia los orígenes antes del corte.',1;
IF EXISTS(SELECT 1 FROM #OpeningCandidates candidate
  JOIN dbo.Receivables bound ON bound.OpeningEntryId=candidate.EntryId
    AND bound.OpeningEntryLineNumber=candidate.LineNumber
    AND NOT(candidate.Kind=N'Receivable' AND candidate.PositionId=bound.ReceivableId))
  OR EXISTS(SELECT 1 FROM #OpeningCandidates candidate
  JOIN dbo.Payables bound ON bound.OpeningEntryId=candidate.EntryId
    AND bound.OpeningEntryLineNumber=candidate.LineNumber
    AND NOT(candidate.Kind=N'Payable' AND candidate.PositionId=bound.PayableId))
  THROW 51871,N'Una partida contable ya está vinculada a otra obligación. Concilia su origen antes del corte.',1;

UPDATE balance SET OpeningEntryId=candidate.EntryId,OpeningEntryLineNumber=candidate.LineNumber
FROM dbo.Receivables balance JOIN #OpeningCandidates candidate
  ON candidate.Kind=N'Receivable' AND candidate.PositionId=balance.ReceivableId
WHERE balance.OpeningEntryId IS NULL AND balance.OpeningEntryLineNumber IS NULL;
UPDATE balance SET OpeningEntryId=candidate.EntryId,OpeningEntryLineNumber=candidate.LineNumber
FROM dbo.Payables balance JOIN #OpeningCandidates candidate
  ON candidate.Kind=N'Payable' AND candidate.PositionId=balance.PayableId
WHERE balance.OpeningEntryId IS NULL AND balance.OpeningEntryLineNumber IS NULL;

IF EXISTS(SELECT 1 FROM dbo.Receivables balance
  JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=balance.SourceDocumentId
    AND job.SourceDocumentType=balance.SourceDocumentType AND job.BusinessId=balance.BusinessId
  LEFT JOIN dbo.Customers customer ON customer.CustomerId=balance.CustomerId AND customer.TenantId=job.TenantId
  CROSS APPLY accounting.ObligationOpeningAccount(job.TenantId,balance.BusinessId,N'Receivable',
    balance.OpeningEntryId,balance.OpeningEntryLineNumber,COALESCE(balance.PartyId,customer.PartyId)) opening
  WHERE job.AccountingEntryRequired=1 AND job.Status=N'Posted' AND opening.Issue IS NOT NULL)
  OR EXISTS(SELECT 1 FROM dbo.Payables balance
  JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=balance.SourceDocumentId
    AND job.SourceDocumentType=balance.SourceDocumentType AND job.BusinessId=balance.BusinessId
  LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=balance.SupplierId AND supplier.TenantId=job.TenantId
  CROSS APPLY accounting.ObligationOpeningAccount(job.TenantId,balance.BusinessId,N'Payable',
    balance.OpeningEntryId,balance.OpeningEntryLineNumber,supplier.PartyId) opening
  WHERE job.AccountingEntryRequired=1 AND job.Status=N'Posted' AND opening.Issue IS NOT NULL)
  THROW 51871,N'Un vínculo histórico no pasó la validación contable compartida. No se modificó la cartera.',1;

DROP TABLE #OpeningCandidates;
DROP TABLE #HistoricalObligations;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
