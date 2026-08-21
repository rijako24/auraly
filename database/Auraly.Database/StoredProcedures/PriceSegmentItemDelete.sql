CREATE PROCEDURE [dbo].[PriceSegmentItemDelete] @Kind NVARCHAR(20),@Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@ProductId UNIQUEIDENTIFIER,@MinimumQuantity DECIMAL(19,6) AS
BEGIN SET NOCOUNT ON;
 IF @Kind=N'PriceList' UPDATE item SET IsActive=0 FROM dbo.PriceListItems item JOIN dbo.PriceLists listValue ON listValue.PriceListId=item.PriceListId WHERE item.PriceListId=@Id AND item.ProductId=@ProductId AND item.MinimumQuantity=@MinimumQuantity AND listValue.BusinessId=@BusinessId AND item.IsActive=1;
 ELSE IF @Kind=N'PriceChannel' BEGIN UPDATE item SET IsActive=0 FROM dbo.ResolvedPriceChannelItems item JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=item.PriceChannelId WHERE item.PriceChannelId=@Id AND item.ProductId=@ProductId AND channelValue.BusinessId=@BusinessId AND item.IsActive=1; DELETE exclusion FROM dbo.PriceChannelExclusions exclusion JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=exclusion.PriceChannelId WHERE exclusion.PriceChannelId=@Id AND exclusion.ProductId=@ProductId AND channelValue.BusinessId=@BusinessId; END
 ELSE THROW 51005,'Invalid price segment kind.',1;
END
