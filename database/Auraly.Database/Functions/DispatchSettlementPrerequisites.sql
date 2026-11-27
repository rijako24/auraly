CREATE FUNCTION dispatch.SettlementPrerequisites(@BusinessId UNIQUEIDENTIFIER, @Expected NVARCHAR(MAX))
RETURNS TABLE
AS RETURN
(
    SELECT COUNT(CASE WHEN financial.SourceDocumentId IS NULL
            OR financial.Status NOT IN(N'Posted',N'CommercialEffectsApplied')
            OR expected.DocumentType=N'SalesReturn' AND (job.JobId IS NULL OR job.Status<>N'Completed')
        THEN 1 END) PendingCount,
        COUNT(CASE WHEN financial.Status=N'AccountingPendingConfiguration'
            OR job.Status IN(N'DeadLettered',N'NeedsIntervention') THEN 1 END) AttentionCount
    FROM OPENJSON(@Expected) WITH (DocumentId uniqueidentifier '$.DocumentId', DocumentType nvarchar(64) '$.DocumentType') expected
    LEFT JOIN dbo.AccountingPostingJobs financial WITH(READCOMMITTEDLOCK)
      ON financial.SourceDocumentId=expected.DocumentId AND financial.SourceDocumentType=expected.DocumentType
      AND financial.BusinessId=@BusinessId
    LEFT JOIN dbo.DocumentProcessingJobs job WITH(READCOMMITTEDLOCK)
      ON job.DocumentId=expected.DocumentId AND job.DocumentType=expected.DocumentType
      AND job.BusinessId=@BusinessId AND expected.DocumentType=N'SalesReturn'
);
