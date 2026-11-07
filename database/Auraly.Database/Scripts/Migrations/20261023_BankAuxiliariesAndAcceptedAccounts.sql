-- Forward-only transition: stop old writers first. The shared legacy auxiliary
-- remains an immutable ledger identity; only future bank selections are split.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

SELECT bank.TenantId,bank.BankAccountId,bank.AccountingAccountId,bank.IsActive BankIsActive,
  COUNT(*) OVER(PARTITION BY bank.TenantId,bank.AccountingAccountId) BankCount
INTO #LegacyBanks
FROM accounting.BankAccounts bank WITH(UPDLOCK,HOLDLOCK)
WHERE bank.AccountingAccountId IS NOT NULL;
CREATE UNIQUE CLUSTERED INDEX IX_LegacyBanks ON #LegacyBanks(TenantId,BankAccountId);

IF EXISTS(SELECT 1 FROM #LegacyBanks bank JOIN dbo.AccountingAccounts account
  ON account.AccountId=bank.AccountingAccountId AND account.TenantId=bank.TenantId
  WHERE bank.BankCount>1 AND account.BankAccountId IS NOT NULL)
  THROW 51734,N'Los bancos comparten un auxiliar ya atribuido a un banco. Concilia el vínculo contradictorio antes del corte.',1;
IF EXISTS(SELECT 1 FROM #LegacyBanks bank
  JOIN dbo.AccountingAccounts account ON account.AccountId=bank.AccountingAccountId AND account.TenantId=bank.TenantId
  WHERE account.AccountType<>N'Asset' OR account.RequiresParty=1
    OR (account.BankAccountId IS NOT NULL AND account.BankAccountId<>bank.BankAccountId)
    OR (account.FinancialFunction IS NOT NULL AND account.FinancialFunction NOT IN(N'General',N'Bank')))
  THROW 51734,N'Un vínculo bancario anterior contradice las propiedades del PUC. Requiere conciliación explícita.',1;
IF EXISTS(SELECT 1 FROM #LegacyBanks legacy JOIN accounting.BankAccounts bank
  ON bank.BankAccountId=legacy.BankAccountId AND bank.TenantId=legacy.TenantId
  WHERE legacy.BankCount>1 AND bank.HistoricalAccountingAccountId IS NOT NULL
    AND bank.HistoricalAccountingAccountId<>legacy.AccountingAccountId)
  THROW 51734,N'El origen bancario histórico ya fijado no coincide con el vínculo anterior.',1;

-- A unique old association remains a valid current configuration. A shared
-- association is retained separately, never assigned arbitrarily to one bank.
UPDATE account SET BankAccountId=bank.BankAccountId,FinancialFunction=N'Bank',BalanceSide=N'Debit'
FROM dbo.AccountingAccounts account JOIN #LegacyBanks bank
  ON bank.AccountingAccountId=account.AccountId AND bank.TenantId=account.TenantId
WHERE bank.BankCount=1 AND account.BankAccountId IS NULL;
UPDATE bank SET HistoricalAccountingAccountId=legacy.AccountingAccountId
FROM accounting.BankAccounts bank JOIN #LegacyBanks legacy
  ON legacy.BankAccountId=bank.BankAccountId AND legacy.TenantId=bank.TenantId
WHERE legacy.BankCount>1 AND bank.HistoricalAccountingAccountId IS NULL;

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
        WHEN job.Status<>N'Posted' THEN CASE WHEN bank.BankCount=1 THEN bank.AccountingAccountId END
        WHEN EXISTS(SELECT 1 FROM dbo.AccountingEntries entry
          JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.AccountId=bank.AccountingAccountId
          WHERE entry.SourceDocumentId=job.SourceDocumentId AND entry.SourceDocumentType=job.SourceDocumentType
            AND entry.TenantId=job.TenantId AND entry.BusinessId=job.BusinessId)
        THEN bank.AccountingAccountId END AccountId
    FROM accounting.AccountingBankReferences(source.PayloadJson) reference
    LEFT JOIN #LegacyBanks bank ON bank.BankAccountId=reference.BankAccountId AND bank.TenantId=job.TenantId
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

-- Provision independent future auxiliaries only where no current relationship
-- already exists. Codes are identifiers, not inferred account classifications.
SELECT legacy.TenantId,legacy.BankAccountId,legacy.AccountingAccountId,legacy.BankIsActive,
  ROW_NUMBER() OVER(PARTITION BY legacy.TenantId,legacy.AccountingAccountId ORDER BY legacy.BankAccountId) BankRank
INTO #FutureBanks
FROM #LegacyBanks legacy
WHERE legacy.BankCount>1 AND NOT EXISTS(
  SELECT 1 FROM dbo.AccountingAccounts account
  WHERE account.TenantId=legacy.TenantId AND account.BankAccountId=legacy.BankAccountId
    AND account.FinancialFunction=N'Bank');

IF EXISTS(SELECT 1 FROM #FutureBanks bank JOIN dbo.AccountingAccounts account
  ON account.AccountId=bank.AccountingAccountId AND account.TenantId=bank.TenantId
  WHERE LEN(account.Code)>30 OR account.Code COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%')
  THROW 51734,N'El código del auxiliar histórico no admite sufijos PUC de compatibilidad. Configura los auxiliares nuevos antes del corte.',1;

;WITH digits(n) AS(SELECT n FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) value(n)),
suffixes(n) AS(SELECT a.n*10+b.n FROM digits a CROSS JOIN digits b WHERE a.n*10+b.n>0),
origins AS(SELECT DISTINCT TenantId,AccountingAccountId FROM #FutureBanks),
available AS(
 SELECT origin.TenantId,origin.AccountingAccountId,
   CONCAT(account.Code,RIGHT(CONCAT(N'0',suffix.n),2)) Code,
   ROW_NUMBER() OVER(PARTITION BY origin.TenantId,origin.AccountingAccountId ORDER BY suffix.n) CodeRank
 FROM origins origin JOIN dbo.AccountingAccounts account
   ON account.AccountId=origin.AccountingAccountId AND account.TenantId=origin.TenantId
 CROSS JOIN suffixes suffix
 WHERE NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts existing
   WHERE existing.TenantId=origin.TenantId AND existing.Code=CONCAT(account.Code,RIGHT(CONCAT(N'0',suffix.n),2)))
)
SELECT * INTO #FutureCodes FROM available;
IF EXISTS(SELECT 1 FROM #FutureBanks bank WHERE NOT EXISTS(
  SELECT 1 FROM #FutureCodes code WHERE code.TenantId=bank.TenantId
    AND code.AccountingAccountId=bank.AccountingAccountId AND code.CodeRank=bank.BankRank))
  THROW 51734,N'No hay suficientes códigos PUC libres para separar los auxiliares bancarios. Configúralos antes del corte.',1;

INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,
  IsActive,CreatedAt,ParentAccountId,FinancialFunction,BalanceSide,BusinessId,BankAccountId,PaymentMethodOptionId)
SELECT NEWID(),future.TenantId,code.Code,LEFT(CONCAT(original.Name,N' · ',bank.DisplayName),200),
  original.AccountType,original.AllowsPosting,original.RequiresParty,
  CONVERT(bit,CASE WHEN original.IsActive=1 AND future.BankIsActive=1 THEN 1 ELSE 0 END),SYSUTCDATETIME(),
  original.ParentAccountId,N'Bank',N'Debit',original.BusinessId,future.BankAccountId,original.PaymentMethodOptionId
FROM #FutureBanks future JOIN #FutureCodes code ON code.TenantId=future.TenantId
  AND code.AccountingAccountId=future.AccountingAccountId AND code.CodeRank=future.BankRank
JOIN dbo.AccountingAccounts original ON original.AccountId=future.AccountingAccountId AND original.TenantId=future.TenantId
JOIN accounting.BankAccounts bank ON bank.BankAccountId=future.BankAccountId AND bank.TenantId=future.TenantId;

UPDATE account SET IsActive=0,ConfigurationLockedAt=COALESCE(ConfigurationLockedAt,SYSUTCDATETIME())
FROM dbo.AccountingAccounts account WHERE EXISTS(
  SELECT 1 FROM #LegacyBanks bank WHERE bank.TenantId=account.TenantId
    AND bank.AccountingAccountId=account.AccountId AND bank.BankCount>1);

UPDATE accounting.BankAccounts SET AccountingAccountId=NULL WHERE AccountingAccountId IS NOT NULL;
DROP TABLE #FutureCodes;
DROP TABLE #FutureBanks;
DROP TABLE #LegacyBanks;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
