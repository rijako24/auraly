CREATE PROCEDURE [dbo].[PriceSegmentItemsList] @Kind NVARCHAR(20),@Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER AS
BEGIN SET NOCOUNT ON;
 IF @Kind=N'PriceList'
  SELECT p.ProductId,p.ProductCode,p.Name,i.Amount,i.CurrencyCode,i.MinimumQuantity,i.ValidFrom,i.ValidUntil,CAST(0 AS bit) FROM dbo.PriceListItems i JOIN dbo.PriceLists l ON l.PriceListId=i.PriceListId JOIN dbo.Products p ON p.ProductId=i.ProductId WHERE i.PriceListId=@Id AND l.BusinessId=@BusinessId AND i.IsActive=1 ORDER BY p.Name,i.MinimumQuantity;
 ELSE IF @Kind=N'PriceChannel'
  SELECT p.ProductId,p.ProductCode,p.Name,i.Amount,i.CurrencyCode,CAST(1 AS decimal(19,6)),i.ValidFrom,i.ValidUntil,CASE WHEN e.ProductId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END FROM dbo.ResolvedPriceChannelItems i JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId JOIN dbo.Products p ON p.ProductId=i.ProductId LEFT JOIN dbo.PriceChannelExclusions e ON e.PriceChannelId=i.PriceChannelId AND e.ProductId=i.ProductId WHERE i.PriceChannelId=@Id AND c.BusinessId=@BusinessId AND i.IsActive=1 ORDER BY p.Name;
 ELSE THROW 51005,'Invalid price segment kind.',1;
END
