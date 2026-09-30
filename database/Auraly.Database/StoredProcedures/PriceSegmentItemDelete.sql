CREATE PROCEDURE [dbo].[PriceSegmentItemDelete] @Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@ProductId UNIQUEIDENTIFIER,@MinimumQuantity DECIMAL(19,6) AS
BEGIN SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels c JOIN dbo.Businesses b ON b.TenantId=c.TenantId
               WHERE c.PriceChannelId=@Id AND b.BusinessId=@BusinessId)
   THROW 51004,'Segment not found',1;
 UPDATE item SET IsActive=0 FROM dbo.PriceChannelItems item WHERE item.PriceChannelId=@Id AND item.ProductId=@ProductId AND item.MinimumQuantity=@MinimumQuantity AND item.IsActive=1;
 SELECT @@ROWCOUNT;
END
