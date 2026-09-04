/*
  FiscalDocuments is created by the DACPAC. Historical invoice snapshots are
  copied only afterwards, when both the source and canonical destination have
  the complete column set. The dynamic batch prevents stale source columns from
  being bound during compilation of an idempotent deployment.
*/
IF OBJECT_ID(N'dbo.FiscalDocuments',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SalesDocuments',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'DocumentId') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'BusinessId') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'DocumentType') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'DocumentNumber') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'FiscalNumber') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'CufeReceived') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'IssuedAt') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'FiscalStatus') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'ReceivedAt') IS NOT NULL
BEGIN
  EXEC sys.sp_executesql N'
    INSERT dbo.FiscalDocuments
      (DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
       AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,
       IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
    SELECT d.DocumentId,d.BusinessId,N''SalesInvoice'',N''Invoice'',
           d.DocumentNumber,d.FiscalNumber,N''CUFE'',d.CufeReceived,
           d.IssuedAt,d.FiscalStatus,d.ReceivedAt,d.ReceivedAt
    FROM dbo.SalesDocuments d
    WHERE d.DocumentType=N''SalesInvoice''
      AND d.FiscalNumber IS NOT NULL
      AND d.FiscalStatus IS NOT NULL
      AND NOT EXISTS
      (SELECT 1 FROM dbo.FiscalDocuments f WHERE f.DocumentId=d.DocumentId);';
END;
