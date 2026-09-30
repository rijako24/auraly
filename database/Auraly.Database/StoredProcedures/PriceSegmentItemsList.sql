CREATE PROCEDURE [dbo].[PriceSegmentItemsList] @Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER AS
BEGIN SET NOCOUNT ON;
 SELECT p.ProductId,p.ProductCode,p.Name,i.Amount,i.CurrencyCode,i.MinimumQuantity FROM dbo.PriceChannelItems i JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId JOIN dbo.Businesses businessValue ON businessValue.TenantId=c.TenantId JOIN dbo.Products p ON p.ProductId=i.ProductId AND p.TenantId=c.TenantId WHERE i.PriceChannelId=@Id AND businessValue.BusinessId=@BusinessId AND i.IsActive=1 ORDER BY p.Name,i.MinimumQuantity;
END
