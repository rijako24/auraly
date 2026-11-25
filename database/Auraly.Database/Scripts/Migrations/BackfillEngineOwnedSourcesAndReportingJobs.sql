SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

INSERT dbo.AccountingSourceDocuments
  (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
   PayloadHash,OccurredAt,AcceptedAt)
SELECT a.SourceDocumentId,a.SourceDocumentType,a.TenantId,a.BusinessId,
       p.PayloadJson,a.SourcePayloadHash,a.OccurredAt,a.CreatedAt
FROM dbo.AccountingPostingJobs a
INNER JOIN dbo.DocumentProcessingPayloads p
  ON p.DocumentId=a.SourceDocumentId
 AND p.DocumentType=a.SourceDocumentType
 AND p.BusinessId=a.BusinessId
 AND p.PayloadHash=a.SourcePayloadHash
WHERE NOT EXISTS
(
  SELECT 1 FROM dbo.AccountingSourceDocuments s
  WHERE s.SourceDocumentId=a.SourceDocumentId
    AND s.SourceDocumentType=a.SourceDocumentType
);

INSERT reporting.SalesReportingJobs
  (SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,
   SourcePayloadHash,SourceDocumentProcessingJobId,Status,AttemptCount,CreatedAt,StartedAt,CompletedAt)
SELECT NEWID(),p.BusinessId,p.DocumentId,p.DocumentType,p.PayloadHash,j.JobId,
       CASE WHEN
         (p.DocumentType IN(N'SalesInvoice',N'SalesReceipt') AND EXISTS
            (SELECT 1 FROM reporting.SalesReportDocuments d
             WHERE d.DocumentId=p.DocumentId AND d.BusinessId=p.BusinessId))
         OR
         (p.DocumentType=N'SalesReturn' AND EXISTS
            (SELECT 1 FROM reporting.SalesReportLineFacts f
             WHERE f.SourceDocumentId=p.DocumentId
               AND f.SourceDocumentType=N'SalesReturn'))
       THEN N'Projected' ELSE N'Pending' END,
       CASE WHEN
         EXISTS(SELECT 1 FROM reporting.SalesReportDocuments d
                WHERE d.DocumentId=p.DocumentId AND d.BusinessId=p.BusinessId)
         OR EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts f
                   WHERE f.SourceDocumentId=p.DocumentId
                     AND f.SourceDocumentType=N'SalesReturn')
       THEN 1 ELSE 0 END,
       p.AcceptedAt,j.StartedAt,
       CASE WHEN
         EXISTS(SELECT 1 FROM reporting.SalesReportDocuments d
                WHERE d.DocumentId=p.DocumentId AND d.BusinessId=p.BusinessId)
         OR EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts f
                   WHERE f.SourceDocumentId=p.DocumentId
                     AND f.SourceDocumentType=N'SalesReturn')
       THEN COALESCE(
         (SELECT MAX(d.ProjectedAt) FROM reporting.SalesReportDocuments d
          WHERE d.DocumentId=p.DocumentId AND d.BusinessId=p.BusinessId),
         (SELECT MAX(f.ProjectedAt) FROM reporting.SalesReportLineFacts f
          WHERE f.SourceDocumentId=p.DocumentId
            AND f.SourceDocumentType=N'SalesReturn'))
       ELSE NULL END
FROM dbo.DocumentProcessingPayloads p
INNER JOIN dbo.DocumentProcessingJobs j
  ON j.DocumentId=p.DocumentId AND j.DocumentType=p.DocumentType
 AND j.BusinessId=p.BusinessId AND j.Status=N'Completed'
INNER JOIN dbo.Businesses owner ON owner.BusinessId=p.BusinessId
WHERE p.DocumentType IN(N'SalesInvoice',N'SalesReceipt',N'SalesReturn')
  AND NOT EXISTS(SELECT 1 FROM OPENJSON(p.PayloadJson) property
    WHERE p.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
      AND p.PayloadHash=HASHBYTES('SHA2_256',CONVERT(varchar(max),p.PayloadJson COLLATE Latin1_General_100_BIN2_UTF8))
      AND TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(p.PayloadJson,'$.documentId'),JSON_VALUE(p.PayloadJson,'$.DocumentId')))=p.DocumentId
      AND TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(p.PayloadJson,'$.businessId'),JSON_VALUE(p.PayloadJson,'$.BusinessId')))=p.BusinessId
      AND TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(p.PayloadJson,'$.tenantId'),JSON_VALUE(p.PayloadJson,'$.TenantId')))=owner.TenantId
      AND NOT EXISTS(SELECT 1 FROM dbo.SalesDocumentLines line WHERE line.DocumentId=p.DocumentId)
      AND NOT EXISTS(SELECT 1 FROM dbo.InventoryMovements movement WHERE movement.DocumentId=p.DocumentId AND movement.BusinessId=p.BusinessId)
      AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries entry WHERE entry.SourceDocumentId=p.DocumentId AND entry.BusinessId=p.BusinessId)
      AND NOT EXISTS(SELECT 1 FROM dbo.Receivables debt WHERE debt.SourceDocumentId=p.DocumentId AND debt.BusinessId=p.BusinessId)
      AND property.[key] COLLATE Latin1_General_100_BIN2 IN(N'fiscalHabilitationOnly',N'FiscalHabilitationOnly')
      AND property.[type]=3 AND property.value=N'true')
  AND NOT EXISTS
  (
    SELECT 1 FROM reporting.SalesReportingJobs r
    WHERE r.SourceDocumentId=p.DocumentId
      AND r.SourceDocumentType=p.DocumentType
  );

COMMIT TRANSACTION;
