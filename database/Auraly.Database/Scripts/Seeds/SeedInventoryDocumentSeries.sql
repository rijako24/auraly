SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
DECLARE @Series TABLE(DocumentType NVARCHAR(64),Prefix NVARCHAR(8));
INSERT @Series VALUES (N'GoodsReceipt',N'EMC'),(N'StockCount',N'CTI'),
  (N'InventoryAdjustment',N'AJI'),(N'WarehouseTransfer',N'TRB'),(N'ProductConversion',N'CNV'),(N'Damage',N'AVE'),
  (N'SalesReturn',N'DVT'),(N'PurchaseReturn',N'DCP'),
  (N'ReceivablePayment',N'RCC'),(N'PayablePayment',N'PGP');
INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
SELECT NEWID(),b.BusinessId,NULL,s.DocumentType,s.Prefix,N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET()
FROM dbo.Businesses b CROSS JOIN @Series s
WHERE NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries ds WHERE ds.BusinessId=b.BusinessId AND ds.DocumentType=s.DocumentType AND ds.DeviceId IS NULL AND ds.IsActive=1);
