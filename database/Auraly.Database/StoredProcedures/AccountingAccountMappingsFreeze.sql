CREATE PROCEDURE accounting.AccountMappingsFreeze
    @TenantId uniqueidentifier,@SourcesJson nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @@TRANCOUNT=0 OR ISNULL(ISJSON(@SourcesJson),0)<>1
      OR LEFT(LTRIM(@SourcesJson),1)<>N'['
      OR (SELECT COUNT(*) FROM OPENJSON(@SourcesJson)) NOT BETWEEN 1 AND 100
      THROW 51731,N'La resolución de cuentas requiere un lote contable válido y su transacción.',1;

    DECLARE @Jobs TABLE(JobId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,OccurredOn date,ExistingJson nvarchar(max));
    INSERT @Jobs
    SELECT job.AccountingPostingJobId,job.BusinessId,CONVERT(date,job.OccurredAt),job.ResolvedAccountMappingsJson
    FROM OPENJSON(@SourcesJson) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(64),BusinessId uniqueidentifier) input
    JOIN dbo.AccountingPostingJobs job WITH(UPDLOCK,HOLDLOCK)
      ON job.SourceDocumentId=input.DocumentId AND job.SourceDocumentType=input.DocumentType
        AND job.BusinessId=input.BusinessId AND job.TenantId=@TenantId
    JOIN reference.Options documentType ON documentType.CatalogCode=N'accounting-document-type'
      AND documentType.Code=job.SourceDocumentType
    WHERE job.AccountingEntryRequired=1 AND job.Status IN(N'Pending',N'AccountingPendingConfiguration')
      AND job.SourceDocumentType NOT IN(N'ManualAccountingVoucher',N'AccountingOpeningBalance',N'RestaurantTipReversal');
    IF NOT EXISTS(SELECT 1 FROM @Jobs) RETURN;

    DECLARE @Frozen TABLE(JobId uniqueidentifier,Category nvarchar(128),AccountId uniqueidentifier NOT NULL,
      PRIMARY KEY(JobId,Category));
    INSERT @Frozen SELECT job.JobId,value.Category,value.AccountId FROM @Jobs job
      CROSS APPLY OPENJSON(job.ExistingJson) WITH(Category nvarchar(128),AccountId uniqueidentifier) value;

    -- Resolve the scoped configuration once per business/date, not once per
    -- document or journal line. The active profile owns its category catalog;
    -- snapshotting that configuration avoids another source-type rule parser.
    DECLARE @Categories TABLE(Category nvarchar(128) PRIMARY KEY);
    INSERT @Categories SELECT DISTINCT category.Category FROM dbo.AccountingConfigurationProfiles profile
      JOIN dbo.AccountingConfigurationProfileAccounts category ON category.ProfileCode=profile.ProfileCode
      WHERE profile.IsDefault=1 AND profile.IsActive=1;
    DECLARE @Scopes TABLE(BusinessId uniqueidentifier,OccurredOn date,PRIMARY KEY(BusinessId,OccurredOn));
    INSERT @Scopes SELECT DISTINCT BusinessId,OccurredOn FROM @Jobs;
    DECLARE @Current TABLE(BusinessId uniqueidentifier,OccurredOn date,Category nvarchar(128),AccountId uniqueidentifier,
      PRIMARY KEY(BusinessId,OccurredOn,Category));
    INSERT @Current
    SELECT scope.BusinessId,scope.OccurredOn,category.Category,configured.AccountId
    FROM @Scopes scope CROSS JOIN @Categories category
    CROSS APPLY accounting.ConfiguredPostingAccountIdentity(@TenantId,scope.BusinessId,category.Category,scope.OccurredOn) configured
    WHERE EXISTS(SELECT 1 FROM @Jobs job WHERE job.BusinessId=scope.BusinessId AND job.OccurredOn=scope.OccurredOn
      AND NOT EXISTS(SELECT 1 FROM @Frozen frozen WHERE frozen.JobId=job.JobId AND frozen.Category=category.Category))
    OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);

    INSERT @Frozen SELECT job.JobId,currentValue.Category,currentValue.AccountId FROM @Jobs job
      JOIN @Current currentValue ON currentValue.BusinessId=job.BusinessId AND currentValue.OccurredOn=job.OccurredOn
      WHERE NOT EXISTS(SELECT 1 FROM @Frozen frozen WHERE frozen.JobId=job.JobId AND frozen.Category=currentValue.Category);

    UPDATE target SET ResolvedAccountMappingsJson=snapshot.Json
    FROM dbo.AccountingPostingJobs target JOIN @Jobs job ON job.JobId=target.AccountingPostingJobId
    CROSS APPLY(SELECT COALESCE((SELECT Category,AccountId FROM @Frozen frozen WHERE frozen.JobId=job.JobId
      ORDER BY Category FOR JSON PATH),N'[]') Json) snapshot
    WHERE target.ResolvedAccountMappingsJson IS NULL OR target.ResolvedAccountMappingsJson<>snapshot.Json;

    -- The snapshot pins mapping identities, not the semantics of unrelated
    -- auxiliaries. Posting validates and protects only the accounts it uses.
END;
GO
