-- Forward-only transition. Stop old writers before applying this schema.
-- Retire inverse values only after proving the accepted history. Leaving them
-- as a second relation would contradict a later allowed edit of an unused PUC.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF EXISTS(SELECT TenantId,AccountingAccountId FROM accounting.BankAccounts
  WHERE AccountingAccountId IS NOT NULL GROUP BY TenantId,AccountingAccountId HAVING COUNT(*)>1)
  THROW 51734,N'Hay bancos que comparten el auxiliar anterior. Concilia sus vínculos antes de habilitar varios auxiliares por banco.',1;
IF EXISTS(SELECT 1 FROM accounting.BankAccounts bank
  JOIN dbo.AccountingAccounts account ON account.AccountId=bank.AccountingAccountId AND account.TenantId=bank.TenantId
  WHERE account.AccountType<>N'Asset' OR account.RequiresParty=1
    OR (account.BankAccountId IS NOT NULL AND account.BankAccountId<>bank.BankAccountId)
    OR (account.FinancialFunction IS NOT NULL AND account.FinancialFunction NOT IN(N'General',N'Bank')))
  THROW 51734,N'Un vínculo bancario anterior contradice las propiedades del PUC. Requiere conciliación explícita.',1;

-- This is a proven legacy relationship, not an inferred account code/parent.
UPDATE account SET BankAccountId=bank.BankAccountId,FinancialFunction=N'Bank',BalanceSide=N'Debit'
FROM dbo.AccountingAccounts account JOIN accounting.BankAccounts bank
  ON bank.AccountingAccountId=account.AccountId AND bank.TenantId=account.TenantId
WHERE account.BankAccountId IS NULL;

DECLARE @BankJobLastId uniqueidentifier=NULL;
CREATE TABLE #BankJobBatch(JobId uniqueidentifier PRIMARY KEY);
WHILE 1=1
BEGIN
  INSERT #BankJobBatch SELECT TOP(100) AccountingPostingJobId FROM dbo.AccountingPostingJobs
  WHERE ResolvedBankAccountsJson IS NULL AND (@BankJobLastId IS NULL OR AccountingPostingJobId>@BankJobLastId)
  ORDER BY AccountingPostingJobId;
  IF @@ROWCOUNT=0 BREAK;

  UPDATE job SET ResolvedBankAccountsJson=COALESCE((
    SELECT reference.BankAccountId,
      CASE WHEN job.AccountingEntryRequired=0 THEN NULL
        WHEN job.Status<>N'Posted' THEN bank.AccountingAccountId
        WHEN EXISTS(SELECT 1 FROM dbo.AccountingEntries entry
          JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.AccountId=bank.AccountingAccountId
          WHERE entry.SourceDocumentId=job.SourceDocumentId AND entry.SourceDocumentType=job.SourceDocumentType
            AND entry.TenantId=job.TenantId AND entry.BusinessId=job.BusinessId)
        THEN bank.AccountingAccountId END AccountId
    FROM accounting.AccountingBankReferences(source.PayloadJson) reference
    LEFT JOIN accounting.BankAccounts bank ON bank.BankAccountId=reference.BankAccountId AND bank.TenantId=job.TenantId
    ORDER BY reference.BankAccountId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')
  FROM #BankJobBatch batch JOIN dbo.AccountingPostingJobs job ON job.AccountingPostingJobId=batch.JobId
  JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=job.SourceDocumentId
    AND source.SourceDocumentType=job.SourceDocumentType AND source.TenantId=job.TenantId AND source.BusinessId=job.BusinessId;

  IF EXISTS(SELECT 1 FROM #BankJobBatch batch
    JOIN dbo.AccountingPostingJobs job ON job.AccountingPostingJobId=batch.JobId
    CROSS APPLY OPENJSON(job.ResolvedBankAccountsJson) WITH(AccountId uniqueidentifier) bank
    WHERE job.AccountingEntryRequired=1 AND job.Status=N'Posted' AND bank.AccountId IS NULL)
    THROW 51734,N'Un documento bancario histórico no permite probar su auxiliar original. Concilia su asiento antes de continuar.',1;

  UPDATE account SET ConfigurationLockedAt=SYSUTCDATETIME()
  FROM dbo.AccountingAccounts account WHERE account.ConfigurationLockedAt IS NULL
    AND EXISTS(SELECT 1 FROM #BankJobBatch batch
      JOIN dbo.AccountingPostingJobs job ON job.AccountingPostingJobId=batch.JobId AND job.TenantId=account.TenantId
      CROSS APPLY OPENJSON(job.ResolvedBankAccountsJson) WITH(AccountId uniqueidentifier) bank
      WHERE job.AccountingEntryRequired=1 AND bank.AccountId=account.AccountId);

  SELECT TOP(1) @BankJobLastId=JobId FROM #BankJobBatch ORDER BY JobId DESC;
  TRUNCATE TABLE #BankJobBatch;
END;
DROP TABLE #BankJobBatch;
UPDATE accounting.BankAccounts SET AccountingAccountId=NULL WHERE AccountingAccountId IS NOT NULL;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
