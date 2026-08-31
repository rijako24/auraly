SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @SalesDocumentSeries TABLE(
  DocumentType NVARCHAR(32) NOT NULL,
  Prefix NVARCHAR(8) NOT NULL);

INSERT @SalesDocumentSeries(DocumentType,Prefix)
VALUES (N'SalesInvoice',N'VTA'),(N'ServiceInvoice',N'FSV'),
       (N'SalesReceipt',N'CVI'),(N'SalesDebitNote',N'NDB');

INSERT dbo.DocumentSeries(
  DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
  Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
SELECT NEWID(),b.BusinessId,NULL,s.DocumentType,s.Prefix,N'00',
       8,1,99999999,0,1,SYSDATETIMEOFFSET()
FROM dbo.Businesses b
CROSS JOIN @SalesDocumentSeries s
WHERE NOT EXISTS(
  SELECT 1 FROM dbo.DocumentSeries ds
  WHERE ds.BusinessId=b.BusinessId AND ds.DocumentType=s.DocumentType
    AND ds.DeviceId IS NULL AND ds.IsActive=1);
