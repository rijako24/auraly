CREATE PROCEDURE dbo.BusinessDocumentSeriesDefaultsProvision
    @BusinessId uniqueidentifier = NULL,
    @Now datetimeoffset(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @Now=COALESCE(@Now,SYSDATETIMEOFFSET());

    IF @BusinessId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId)
        THROW 51043,'La sede indicada no existe.',1;

    DECLARE @Series TABLE(DocumentType nvarchar(64),Prefix nvarchar(8));
    INSERT @Series VALUES
      (N'SalesInvoice',N'VTA'),(N'SalesReceipt',N'CVI'),(N'SalesDebitNote',N'NDB'),
      (N'GoodsReceipt',N'EMC'),(N'PurchaseOrder',N'OCP'),
      (N'SalesReturn',N'DVT'),(N'PurchaseReturn',N'DCP'),
      (N'ReceivablePayment',N'RCC'),(N'PayablePayment',N'PGP'),
      (N'StockCount',N'CTI'),(N'InventoryAdjustment',N'AJI'),
      (N'WarehouseTransfer',N'TRB'),(N'ProductConversion',N'CNV'),(N'Damage',N'AVE');

    INSERT dbo.DocumentSeries(
        DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
        Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
    SELECT NEWID(),business.BusinessId,NULL,series.DocumentType,series.Prefix,N'00',
           8,1,99999999,0,1,@Now
    FROM dbo.Businesses business
    CROSS JOIN @Series series
    WHERE (@BusinessId IS NULL OR business.BusinessId=@BusinessId)
      AND NOT EXISTS(
        SELECT 1
        FROM dbo.DocumentSeries currentSeries
        WHERE currentSeries.BusinessId=business.BusinessId
          AND currentSeries.DocumentType=series.DocumentType
          AND currentSeries.DeviceId IS NULL
          AND currentSeries.IsActive=1);
END;
