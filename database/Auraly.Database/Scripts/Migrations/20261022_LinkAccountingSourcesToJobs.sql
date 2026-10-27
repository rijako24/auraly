-- Link immutable sources only to already existing, identically scoped work.
-- No jobs, entries, balances, modes or source payloads are created or repaired.
DECLARE @SourceJobLastId uniqueidentifier=NULL,@SourceJobLastType nvarchar(64)=NULL;
CREATE TABLE #SourceJobLinkBatch(SourceDocumentId uniqueidentifier,SourceDocumentType nvarchar(64) COLLATE DATABASE_DEFAULT,
  TenantId uniqueidentifier,BusinessId uniqueidentifier,PRIMARY KEY(SourceDocumentId,SourceDocumentType));
WHILE 1=1
BEGIN
  INSERT #SourceJobLinkBatch
  SELECT TOP(1000) SourceDocumentId,SourceDocumentType,TenantId,BusinessId
  FROM dbo.AccountingSourceDocuments
  WHERE AccountingPostingJobId IS NULL AND (@SourceJobLastId IS NULL
    OR SourceDocumentId>@SourceJobLastId
    OR (SourceDocumentId=@SourceJobLastId AND SourceDocumentType>@SourceJobLastType))
  ORDER BY SourceDocumentId,SourceDocumentType OPTION(RECOMPILE);
  IF @@ROWCOUNT=0 BREAK;

  UPDATE source SET AccountingPostingJobId=job.AccountingPostingJobId
  FROM #SourceJobLinkBatch batch
  JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=batch.SourceDocumentId
    AND source.SourceDocumentType=batch.SourceDocumentType
  JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=batch.SourceDocumentId
    AND job.SourceDocumentType=batch.SourceDocumentType
    AND job.TenantId=batch.TenantId AND job.BusinessId=batch.BusinessId
  WHERE source.AccountingPostingJobId IS NULL;

  SELECT TOP(1) @SourceJobLastId=SourceDocumentId,@SourceJobLastType=SourceDocumentType
  FROM #SourceJobLinkBatch ORDER BY SourceDocumentId DESC,SourceDocumentType DESC;
  TRUNCATE TABLE #SourceJobLinkBatch;
END;
DROP TABLE #SourceJobLinkBatch;
GO
