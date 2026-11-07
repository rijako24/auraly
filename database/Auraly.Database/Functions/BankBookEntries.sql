-- One read owner for current auxiliaries and proven shared-auxiliary history.
-- Unproven history remains visible to authorization and blocks bank reconciliation.
CREATE FUNCTION accounting.BankBookEntries(@TenantId uniqueidentifier,@BankAccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    SELECT line.EntryId,line.LineNumber,CONVERT(bit,1) HasBankAttribution
    FROM accounting.BankBookAccounts(@TenantId,@BankAccountId) account
    JOIN dbo.AccountingEntryLines line ON line.AccountId=account.AccountId
    JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId AND entry.TenantId=@TenantId
    UNION ALL
    SELECT line.EntryId,line.LineNumber,CONVERT(bit,CASE WHEN attribution.BankCount=1 THEN 1 ELSE 0 END)
    FROM accounting.BankAccounts bank
    JOIN dbo.AccountingEntryLines line ON line.AccountId=bank.HistoricalAccountingAccountId
    JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId AND entry.TenantId=bank.TenantId
    LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=entry.SourceDocumentId
      AND job.SourceDocumentType=entry.SourceDocumentType AND job.BusinessId=entry.BusinessId
      AND job.TenantId=entry.TenantId AND job.SourcePayloadHash=entry.SourcePayloadHash
      AND job.Status=N'Posted' AND job.AccountingEntryRequired=1
    OUTER APPLY(
      SELECT COUNT(DISTINCT value.BankAccountId) BankCount,
        MAX(CONVERT(nvarchar(36),value.BankAccountId)) BankId
      FROM OPENJSON(job.ResolvedBankAccountsJson)
        WITH(BankAccountId uniqueidentifier,AccountId uniqueidentifier) value
      JOIN accounting.BankAccounts owner ON owner.BankAccountId=value.BankAccountId AND owner.TenantId=@TenantId
      WHERE value.AccountId=line.AccountId
    ) attribution
    WHERE bank.TenantId=@TenantId AND bank.BankAccountId=@BankAccountId
      AND bank.HistoricalAccountingAccountId IS NOT NULL
      AND (attribution.BankCount<>1 OR attribution.BankId=CONVERT(nvarchar(36),@BankAccountId))
);
GO
