CREATE PROCEDURE [dbo].[PriceSegmentItemDelete] @Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@ProductId UNIQUEIDENTIFIER,@MinimumQuantity DECIMAL(19,6) AS
BEGIN SET NOCOUNT ON;
 UPDATE item SET IsActive=0 FROM dbo.ResolvedPriceChannelItems item JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=item.PriceChannelId WHERE item.PriceChannelId=@Id AND item.ProductId=@ProductId AND item.MinimumQuantity=@MinimumQuantity AND channelValue.BusinessId=@BusinessId AND item.IsActive=1;
END
