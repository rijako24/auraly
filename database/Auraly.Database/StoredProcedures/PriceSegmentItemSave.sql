CREATE PROCEDURE [dbo].[PriceSegmentItemSave] @Kind NVARCHAR(20),@Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@ProductId UNIQUEIDENTIFIER,@MinimumQuantity DECIMAL(19,6),@Amount DECIMAL(19,4),@ValidFrom DATETIMEOFFSET(7),@ValidUntil DATETIMEOFFSET(7)=NULL,@Excluded BIT AS
BEGIN SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1) THROW 51004,'Product not found',1;
 IF @Kind=N'PriceList' BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.PriceLists WHERE PriceListId=@Id AND BusinessId=@BusinessId) THROW 51004,'Segment not found',1;
  UPDATE dbo.PriceListItems SET IsActive=0 WHERE PriceListId=@Id AND ProductId=@ProductId AND MinimumQuantity=@MinimumQuantity AND IsActive=1;
  INSERT dbo.PriceListItems(PriceListItemId,PriceListId,ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt) VALUES(NEWID(),@Id,@ProductId,@MinimumQuantity,@Amount,N'COP',@ValidFrom,@ValidUntil,1,SYSUTCDATETIME());
 END ELSE IF @Kind=N'PriceChannel' BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Id AND BusinessId=@BusinessId) THROW 51004,'Segment not found',1;
  UPDATE dbo.ResolvedPriceChannelItems SET IsActive=0 WHERE PriceChannelId=@Id AND ProductId=@ProductId AND IsActive=1;
  INSERT dbo.ResolvedPriceChannelItems(ResolvedPriceChannelItemId,PriceChannelId,ProductId,Amount,CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt) VALUES(NEWID(),@Id,@ProductId,@Amount,N'COP',@ValidFrom,@ValidUntil,1,SYSUTCDATETIME());
  DELETE dbo.PriceChannelExclusions WHERE PriceChannelId=@Id AND ProductId=@ProductId;
  IF @Excluded=1 INSERT dbo.PriceChannelExclusions(PriceChannelId,ProductId,CreatedAt) VALUES(@Id,@ProductId,SYSUTCDATETIME());
 END ELSE THROW 51005,'Invalid price segment kind.',1;
END
