CREATE PROCEDURE accounting.BankAccountsFreeze
    @TenantId uniqueidentifier,@SourcesJson nvarchar(max),@Now datetimeoffset(7),
    @MissingCount int=NULL OUTPUT,@SingleResolvedJson nvarchar(max)=NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @@TRANCOUNT=0 OR ISNULL(ISJSON(@SourcesJson),0)<>1
      OR LEFT(LTRIM(@SourcesJson),1)<>N'['
      OR (SELECT COUNT(*) FROM OPENJSON(@SourcesJson)) NOT BETWEEN 1 AND 100
      THROW 51731,N'La resolución bancaria requiere un lote contable válido y su transacción.',1;

    DECLARE @Jobs TABLE(JobId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,
      SourceDocumentId uniqueidentifier,SourceDocumentType nvarchar(64),Payload nvarchar(max),
      ExistingJson nvarchar(max),ResolvedJson nvarchar(max));
    INSERT @Jobs(JobId,BusinessId,SourceDocumentId,SourceDocumentType,Payload,ExistingJson)
    SELECT job.AccountingPostingJobId,job.BusinessId,job.SourceDocumentId,job.SourceDocumentType,
      source.PayloadJson,job.ResolvedBankAccountsJson
    FROM OPENJSON(@SourcesJson) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(64),BusinessId uniqueidentifier) input
    JOIN dbo.AccountingPostingJobs job WITH(UPDLOCK,HOLDLOCK)
      ON job.SourceDocumentId=input.DocumentId AND job.SourceDocumentType=input.DocumentType
        AND job.BusinessId=input.BusinessId AND job.TenantId=@TenantId
    JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=job.SourceDocumentId
      AND source.SourceDocumentType=job.SourceDocumentType AND source.TenantId=job.TenantId AND source.BusinessId=job.BusinessId
    -- Reversals use the original journal, including legacy journals without a
    -- bank snapshot. They never need a new bank/category selection.
    WHERE job.AccountingEntryRequired=1 AND job.Status IN(N'Pending',N'AccountingPendingConfiguration')
      AND job.SourceDocumentType<>N'RestaurantTipReversal';

    DECLARE @Resolved TABLE(JobId uniqueidentifier,BankAccountId uniqueidentifier,AccountId uniqueidentifier,
      PRIMARY KEY(JobId,BankAccountId));
    INSERT @Resolved
    SELECT job.JobId,bank.BankAccountId,COALESCE(frozen.AccountId,configured.AccountId)
    FROM @Jobs job
    CROSS APPLY accounting.AccountingBankReferences(job.Payload) bank
    OUTER APPLY(SELECT value.AccountId FROM OPENJSON(job.ExistingJson)
      WITH(BankAccountId uniqueidentifier,AccountId uniqueidentifier) value WHERE value.BankAccountId=bank.BankAccountId) frozen
    OUTER APPLY accounting.BankPostingAccount(@TenantId,job.BusinessId,bank.BankAccountId) configured;

    UPDATE job SET ResolvedJson=COALESCE((SELECT BankAccountId,AccountId FROM @Resolved resolved
      WHERE resolved.JobId=job.JobId ORDER BY BankAccountId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')
    FROM @Jobs job;
    UPDATE target SET ResolvedBankAccountsJson=job.ResolvedJson
    FROM dbo.AccountingPostingJobs target JOIN @Jobs job ON job.JobId=target.AccountingPostingJobId
    WHERE target.ResolvedBankAccountsJson IS NULL OR target.ResolvedBankAccountsJson<>job.ResolvedJson;
    UPDATE account SET ConfigurationLockedAt=@Now
    FROM dbo.AccountingAccounts account
    WHERE account.TenantId=@TenantId AND account.ConfigurationLockedAt IS NULL
      AND EXISTS(SELECT 1 FROM @Resolved resolved WHERE resolved.AccountId=account.AccountId);

    SELECT @MissingCount=COUNT(*) FROM @Resolved WHERE AccountId IS NULL;
    SELECT @SingleResolvedJson=CASE WHEN COUNT(*)=1 THEN MAX(ResolvedJson) END FROM @Jobs;
END;
GO
