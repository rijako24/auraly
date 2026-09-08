SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- Before this release, commercial credit created while accounting was disabled
-- intentionally had a Receivable tied only to the operational job. Its own
-- existence proves that the commercial effect already happened; it must not be
-- inferred as an accounting entry after a later activation.
INSERT dbo.AccountingSourceDocuments
  (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
   PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
SELECT receivable.SourceDocumentId,receivable.SourceDocumentType,business.TenantId,
       receivable.BusinessId,COALESCE(payload.PayloadJson,N'{}'),
       COALESCE(payload.PayloadHash,sale.PayloadHash),sale.IssuedAt,
       COALESCE(payload.AcceptedAt,sale.ReceivedAt),0
FROM dbo.Receivables receivable
INNER JOIN dbo.Businesses business
  ON business.BusinessId=receivable.BusinessId
INNER JOIN dbo.SalesDocuments sale
  ON sale.DocumentId=receivable.SourceDocumentId
 AND sale.BusinessId=receivable.BusinessId
 AND sale.DocumentType=receivable.SourceDocumentType
LEFT JOIN dbo.DocumentProcessingPayloads payload
  ON payload.DocumentId=receivable.SourceDocumentId
 AND payload.DocumentType=receivable.SourceDocumentType
 AND payload.BusinessId=receivable.BusinessId
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.AccountingSourceDocuments source
    WHERE source.SourceDocumentId=receivable.SourceDocumentId
      AND source.SourceDocumentType=receivable.SourceDocumentType
);

IF EXISTS
(
    SELECT 1 FROM dbo.Receivables receivable
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.AccountingSourceDocuments source
        WHERE source.SourceDocumentId=receivable.SourceDocumentId
          AND source.SourceDocumentType=receivable.SourceDocumentType
    )
)
    THROW 51407,N'A receivable has no provable immutable sales source.',1;

INSERT dbo.AccountingPostingJobs
  (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
   SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,
   Status,AttemptCount,CreatedAt,CompletedAt)
SELECT NEWID(),source.TenantId,source.BusinessId,source.SourceDocumentId,
       source.SourceDocumentType,source.PayloadHash,source.OccurredAt,
       COALESCE(source.AccountingEntryRequired,0),
       CASE WHEN source.AccountingEntryRequired=1
            THEN N'Pending' ELSE N'CommercialEffectsApplied' END,
       CASE WHEN source.AccountingEntryRequired=1 THEN 0 ELSE 1 END,
       source.AcceptedAt,
       CASE WHEN source.AccountingEntryRequired=1 THEN NULL ELSE source.AcceptedAt END
FROM dbo.AccountingSourceDocuments source
INNER JOIN dbo.Receivables receivable
  ON receivable.SourceDocumentId=source.SourceDocumentId
 AND receivable.SourceDocumentType=source.SourceDocumentType
 AND receivable.BusinessId=source.BusinessId
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.AccountingPostingJobs job
    WHERE job.SourceDocumentId=source.SourceDocumentId
      AND job.SourceDocumentType=source.SourceDocumentType
);

UPDATE source
SET AccountingEntryRequired=job.AccountingEntryRequired
FROM dbo.AccountingSourceDocuments source
INNER JOIN dbo.AccountingPostingJobs job
  ON job.SourceDocumentId=source.SourceDocumentId
 AND job.SourceDocumentType=source.SourceDocumentType
WHERE source.AccountingEntryRequired IS NULL;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.Receivables')
      AND name=N'FK_Receivables_SourceJob'
)
    ALTER TABLE dbo.Receivables DROP CONSTRAINT FK_Receivables_SourceJob;
GO

ALTER TABLE dbo.Receivables WITH CHECK
  ADD CONSTRAINT FK_Receivables_SourceJob
  FOREIGN KEY (SourceDocumentId,SourceDocumentType)
  REFERENCES dbo.AccountingPostingJobs(SourceDocumentId,SourceDocumentType);
GO

COMMIT TRANSACTION;
GO
