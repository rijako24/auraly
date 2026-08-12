SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

INSERT dbo.DocumentSeries(
  DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
  Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
SELECT NEWID(),b.BusinessId,NULL,N'SalesReceipt',N'CVI',N'00',
       8,1,99999999,0,1,SYSDATETIMEOFFSET()
FROM dbo.Businesses b
WHERE NOT EXISTS(
  SELECT 1 FROM dbo.DocumentSeries ds
  WHERE ds.BusinessId=b.BusinessId AND ds.DocumentType=N'SalesReceipt'
    AND ds.DeviceId IS NULL AND ds.IsActive=1);

