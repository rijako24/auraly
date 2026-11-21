SET NOCOUNT ON;
SET XACT_ABORT ON;

-- The release pipeline runs existing-state migrations before DACPAC planning.
-- Fresh databases have no reporting queue yet.
IF OBJECT_ID(N'reporting.SalesReportingJobs',N'U') IS NULL RETURN;

BEGIN TRANSACTION;

IF COL_LENGTH(N'reporting.SalesReportingJobs',N'SourceDocumentProcessingJobId') IS NULL
    EXEC(N'ALTER TABLE reporting.SalesReportingJobs ADD SourceDocumentProcessingJobId uniqueidentifier NULL;');
IF COL_LENGTH(N'reporting.SalesReportingJobs',N'SourcePayloadJson') IS NULL
    EXEC(N'ALTER TABLE reporting.SalesReportingJobs ADD SourcePayloadJson nvarchar(max) NULL;');

-- Defer binding until the historical shape above is aligned. Only restore an
-- existing immutable source; never regenerate payloads or run an operation.
EXEC sys.sp_executesql N'
    UPDATE report SET SourceDocumentProcessingJobId=job.JobId
    FROM reporting.SalesReportingJobs report
    JOIN dbo.DocumentProcessingPayloads payload
      ON payload.DocumentId=report.SourceDocumentId
     AND payload.DocumentType=report.SourceDocumentType
     AND payload.BusinessId=report.BusinessId
     AND payload.PayloadHash=report.SourcePayloadHash
    JOIN dbo.DocumentProcessingJobs job
      ON job.DocumentId=payload.DocumentId
     AND job.DocumentType=payload.DocumentType
     AND job.BusinessId=payload.BusinessId
    WHERE report.SourceDocumentProcessingJobId IS NULL AND report.SourcePayloadJson IS NULL;

    IF EXISTS(SELECT 1 FROM reporting.SalesReportingJobs
      WHERE SourceDocumentProcessingJobId IS NULL AND SourcePayloadJson IS NULL)
      THROW 51623,N''Hay fuentes históricas de Reporting sin un origen inmutable verificable. Revisa sus documentos antes de desplegar.'',1;
';

COMMIT TRANSACTION;
GO
