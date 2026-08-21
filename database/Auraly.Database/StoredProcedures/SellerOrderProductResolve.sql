CREATE PROCEDURE [dbo].[SellerOrderProductResolve]
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER,
    @Quantity DECIMAL(19,6)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),p.Name,
           COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),COALESCE(listPrice.Amount,channelPrice.Amount,basePrice.Amount),
           CASE WHEN listPrice.Amount IS NOT NULL THEN N'PriceList' WHEN channelPrice.Amount IS NOT NULL THEN N'PriceChannel' ELSE N'Public' END,
           COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=p.BusinessId AND m.WarehouseId=@WarehouseId AND m.ProductId=p.ProductId),0),
           p.ManageStock,COALESCE(tax.Rate,0)
    FROM dbo.Products p
    LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=p.TaxProfileId AND tax.IsActive=1
    CROSS APPLY(SELECT TOP(1) pp.Amount FROM dbo.ProductPrices pp WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId AND pp.IsActive=1 AND pp.ValidFrom<=SYSDATETIMEOFFSET() AND(pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY pp.ValidFrom DESC)basePrice
    LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=@CustomerId
    OUTER APPLY(SELECT TOP(1)i.Amount FROM dbo.PriceListItems i JOIN dbo.PriceLists l ON l.PriceListId=i.PriceListId WHERE i.PriceListId=setting.PriceListId AND l.BusinessId=@BusinessId AND l.IsActive=1 AND i.ProductId=p.ProductId AND i.IsActive=1 AND i.MinimumQuantity<=@Quantity AND i.ValidFrom<=SYSDATETIMEOFFSET() AND(i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY i.MinimumQuantity DESC,i.ValidFrom DESC)listPrice
    OUTER APPLY(SELECT TOP(1)i.Amount FROM dbo.ResolvedPriceChannelItems i JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId WHERE i.PriceChannelId=setting.PriceChannelId AND c.BusinessId=@BusinessId AND c.IsActive=1 AND i.ProductId=p.ProductId AND i.IsActive=1 AND i.ValidFrom<=SYSDATETIMEOFFSET() AND(i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET()) AND NOT EXISTS(SELECT 1 FROM dbo.PriceChannelExclusions e WHERE e.PriceChannelId=i.PriceChannelId AND e.ProductId=i.ProductId))channelPrice
    WHERE p.BusinessId=@BusinessId AND p.ProductId=@ProductId AND p.IsActive=1;
END
