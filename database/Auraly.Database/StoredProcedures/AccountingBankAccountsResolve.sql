CREATE PROCEDURE accounting.BankAccountsResolve
    @TenantId uniqueidentifier,@SourcesJson nvarchar(max),
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
      ResolvedJson nvarchar(max));
    INSERT @Jobs(JobId,BusinessId,SourceDocumentId,SourceDocumentType,Payload)
    SELECT job.AccountingPostingJobId,job.BusinessId,job.SourceDocumentId,job.SourceDocumentType,
      source.PayloadJson
    FROM OPENJSON(@SourcesJson) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(64),BusinessId uniqueidentifier) input
    JOIN dbo.AccountingPostingJobs job WITH(UPDLOCK,HOLDLOCK)
      ON job.SourceDocumentId=input.DocumentId AND job.SourceDocumentType=input.DocumentType
        AND job.BusinessId=input.BusinessId AND job.TenantId=@TenantId
    JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=job.SourceDocumentId
      AND source.SourceDocumentType=job.SourceDocumentType AND source.TenantId=job.TenantId AND source.BusinessId=job.BusinessId
    WHERE job.AccountingEntryRequired=1 AND job.Status IN(N'Pending',N'AccountingPendingConfiguration');

    -- Reversal attribution is evidence of its original posted journal, never
    -- a selection from current treasury configuration. Legacy NULL stays unknown.
    UPDATE reversal SET ResolvedJson=original.ResolvedBankAccountsJson
    FROM @Jobs reversal
    JOIN dbo.AccountingPostingJobs original ON original.SourceDocumentId=COALESCE(
        TRY_CONVERT(uniqueidentifier,JSON_VALUE(reversal.Payload,'$.CollectionId')),
        TRY_CONVERT(uniqueidentifier,JSON_VALUE(reversal.Payload,'$.collectionId')))
      AND original.SourceDocumentType=N'RestaurantTip' AND original.TenantId=@TenantId
      AND original.BusinessId=reversal.BusinessId AND original.Status=N'Posted'
    JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=original.SourceDocumentId
      AND entry.SourceDocumentType=original.SourceDocumentType AND entry.TenantId=original.TenantId
      AND entry.BusinessId=original.BusinessId AND entry.SourcePayloadHash=original.SourcePayloadHash
    WHERE reversal.SourceDocumentType=N'RestaurantTipReversal';

    DECLARE @Resolved TABLE(JobId uniqueidentifier,BankAccountId uniqueidentifier,AccountId uniqueidentifier,
      PRIMARY KEY(JobId,BankAccountId));
    -- Bound configuration resolution to the actual bank references first.
    -- Direct-account/manual and configured-form sources commonly have none;
    -- they still receive their authoritative empty bank result below.
    DECLARE @ReferencedBanks TABLE(JobId uniqueidentifier,BankAccountId uniqueidentifier,
      PRIMARY KEY(JobId,BankAccountId));
    INSERT @ReferencedBanks SELECT job.JobId,bank.BankAccountId
    FROM @Jobs job CROSS APPLY accounting.AccountingBankReferences(job.Payload) bank
    WHERE job.SourceDocumentType<>N'RestaurantTipReversal';
    IF EXISTS(SELECT 1 FROM @ReferencedBanks)
    BEGIN
    INSERT @Resolved
    SELECT job.JobId,bank.BankAccountId,configured.AccountId
    FROM @ReferencedBanks bank JOIN @Jobs job ON job.JobId=bank.JobId
    OUTER APPLY accounting.BankPostingAccount(@TenantId,bank.BankAccountId) configured;
    END;

    UPDATE job SET ResolvedJson=COALESCE((SELECT BankAccountId,AccountId FROM @Resolved resolved
      WHERE resolved.JobId=job.JobId ORDER BY BankAccountId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')
    FROM @Jobs job WHERE job.SourceDocumentType<>N'RestaurantTipReversal';
    SELECT @MissingCount=COUNT(*) FROM @Resolved WHERE AccountId IS NULL;
    SELECT @SingleResolvedJson=CASE WHEN COUNT(*)=1 THEN COALESCE(MAX(ResolvedJson),N'[]') END FROM @Jobs;
END;
GO
