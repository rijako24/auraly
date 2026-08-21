CREATE PROCEDURE [dbo].[PriceSegmentsList] @BusinessId UNIQUEIDENTIFIER AS
BEGIN SET NOCOUNT ON;
 SELECT l.PriceListId,N'PriceList',l.Code,l.Name,l.IsActive,l.CreatedAt,(SELECT COUNT(DISTINCT i.ProductId) FROM dbo.PriceListItems i WHERE i.PriceListId=l.PriceListId AND i.IsActive=1),(SELECT COUNT(*) FROM dbo.CustomerPricingSettings s JOIN dbo.Customers c ON c.CustomerId=s.CustomerId WHERE c.BusinessId=l.BusinessId AND s.PriceListId=l.PriceListId) FROM dbo.PriceLists l WHERE l.BusinessId=@BusinessId
 UNION ALL
 SELECT c.PriceChannelId,N'PriceChannel',c.Code,c.Name,c.IsActive,c.CreatedAt,(SELECT COUNT(*) FROM dbo.ResolvedPriceChannelItems i WHERE i.PriceChannelId=c.PriceChannelId AND i.IsActive=1),(SELECT COUNT(*) FROM dbo.CustomerPricingSettings s JOIN dbo.Customers customer ON customer.CustomerId=s.CustomerId WHERE customer.BusinessId=c.BusinessId AND s.PriceChannelId=c.PriceChannelId) FROM dbo.PriceChannels c WHERE c.BusinessId=@BusinessId ORDER BY 2,4;
END
