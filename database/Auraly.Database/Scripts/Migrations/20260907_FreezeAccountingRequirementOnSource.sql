IF COL_LENGTH(N'dbo.AccountingSourceDocuments',N'AccountingEntryRequired') IS NULL
    ALTER TABLE dbo.AccountingSourceDocuments ADD AccountingEntryRequired BIT NULL;
GO

UPDATE source
SET AccountingEntryRequired=job.AccountingEntryRequired
FROM dbo.AccountingSourceDocuments source
INNER JOIN dbo.AccountingPostingJobs job
  ON job.SourceDocumentId=source.SourceDocumentId
 AND job.SourceDocumentType=source.SourceDocumentType
 AND job.TenantId=source.TenantId
 AND job.BusinessId=source.BusinessId
WHERE source.AccountingEntryRequired IS NULL;
GO
