CREATE PROCEDURE [dbo].[PriceSegmentItemSave] @Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@ProductId UNIQUEIDENTIFIER,@MinimumQuantity DECIMAL(19,6),@Amount DECIMAL(19,4) AS
BEGIN SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1) THROW 51004,'Product not found',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Id AND BusinessId=@BusinessId) THROW 51004,'Segment not found',1;
  UPDATE dbo.ResolvedPriceChannelItems SET IsActive=0 WHERE PriceChannelId=@Id AND ProductId=@ProductId AND MinimumQuantity=@MinimumQuantity AND IsActive=1;
  INSERT dbo.ResolvedPriceChannelItems(ResolvedPriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt) VALUES(NEWID(),@Id,@ProductId,@MinimumQuantity,@Amount,N'COP',SYSUTCDATETIME(),NULL,1,SYSUTCDATETIME());
END
