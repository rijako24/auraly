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

    -- Restore the original Reporting input only from an already accepted,
    -- byte-verified snapshot with the same immutable hash and envelope identity.
    -- Keep operational/fiscal/financial documents and their hashes untouched.
    IF OBJECT_ID(N''dbo.AccountingSourceDocuments'',N''U'') IS NOT NULL
    BEGIN
        UPDATE report SET SourceDocumentProcessingJobId=NULL,SourcePayloadJson=accepted.PayloadJson
        FROM reporting.SalesReportingJobs report
        JOIN dbo.AccountingSourceDocuments accepted
          ON accepted.SourceDocumentId=report.SourceDocumentId
         AND accepted.SourceDocumentType=report.SourceDocumentType
         AND accepted.BusinessId=report.BusinessId AND accepted.PayloadHash=report.SourcePayloadHash
        JOIN dbo.Businesses owner ON owner.BusinessId=accepted.BusinessId AND owner.TenantId=accepted.TenantId
        JOIN dbo.SalesDocuments sale ON sale.DocumentId=report.SourceDocumentId
          AND sale.BusinessId=owner.BusinessId AND sale.DocumentType=report.SourceDocumentType
        LEFT JOIN dbo.DocumentProcessingPayloads payload
          ON payload.DocumentId=report.SourceDocumentId AND payload.DocumentType=report.SourceDocumentType
         AND payload.BusinessId=report.BusinessId AND payload.PayloadHash=report.SourcePayloadHash
        CROSS APPLY (VALUES(CASE WHEN ISJSON(accepted.PayloadJson)=1 THEN accepted.PayloadJson ELSE N''{}'' END)) evidence(Json)
        WHERE report.SourcePayloadJson IS NULL AND report.SourceDocumentType IN(N''SalesInvoice'',N''SalesReceipt'')
          AND EXISTS(SELECT 1 FROM dbo.DocumentProcessingJobs operation WHERE operation.DocumentId=report.SourceDocumentId
            AND operation.DocumentType=report.SourceDocumentType AND operation.BusinessId=report.BusinessId AND operation.Status=N''Completed'')
          AND accepted.PayloadHash=HASHBYTES(''SHA2_256'',CONVERT(varchar(max),accepted.PayloadJson COLLATE Latin1_General_100_BIN2_UTF8))
          AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(evidence.Json,''$.documentId''))=report.SourceDocumentId
          AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(evidence.Json,''$.tenantId''))=owner.TenantId
          AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(evidence.Json,''$.businessId''))=owner.BusinessId
          AND JSON_VALUE(evidence.Json,''$.commercialSnapshot.documentType'') COLLATE Latin1_General_100_BIN2
            =report.SourceDocumentType COLLATE Latin1_General_100_BIN2
          AND (payload.PayloadJson IS NULL OR report.SourcePayloadHash<>
            HASHBYTES(''SHA2_256'',CONVERT(varchar(max),payload.PayloadJson COLLATE Latin1_General_100_BIN2_UTF8)));
    END;

    IF EXISTS(SELECT 1 FROM reporting.SalesReportingJobs
      WHERE SourceDocumentProcessingJobId IS NULL AND SourcePayloadJson IS NULL)
      THROW 51623,N''Hay fuentes históricas de Reporting sin un origen inmutable verificable. Revisa sus documentos antes de desplegar.'',1;

    IF EXISTS(SELECT 1 FROM reporting.SalesReportingJobs report
      LEFT JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=report.SourceDocumentId
        AND payload.DocumentType=report.SourceDocumentType AND payload.BusinessId=report.BusinessId
        AND payload.PayloadHash=report.SourcePayloadHash
      WHERE COALESCE(report.SourcePayloadJson,payload.PayloadJson) IS NULL OR report.SourcePayloadHash<>
        HASHBYTES(''SHA2_256'',CONVERT(varchar(max),COALESCE(report.SourcePayloadJson,payload.PayloadJson) COLLATE Latin1_General_100_BIN2_UTF8)))
      THROW 51629,N''El contenido de una fuente de Reporting no coincide con su huella inmutable. No se modificaron sus referencias.'',1;
';

COMMIT TRANSACTION;
GO
