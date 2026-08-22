CREATE PROCEDURE [dbo].[SellerOrderCatalogGet]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @Search NVARCHAR(250),
    @Contains NVARCHAR(252),
    @Prefix NVARCHAR(251),
    @Skip INT,
    @Take INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.Businesses b
        INNER JOIN dbo.Warehouses w ON w.BusinessId = b.BusinessId
        INNER JOIN dbo.Customers c ON c.BusinessId = b.BusinessId
        WHERE b.BusinessId = @BusinessId AND b.TenantId = @TenantId
          AND w.WarehouseId = @WarehouseId AND w.IsActive = 1 AND w.UseForSales = 1
          AND c.CustomerId = @CustomerId AND c.IsActive = 1)
        THROW 51300, 'Selecciona una bodega de venta válida.', 1;

    SELECT p.ProductId,
           COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),
           p.Name,
           COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
           COALESCE(channelPrice.Amount,basePrice.Amount),
           CASE WHEN channelPrice.Amount IS NOT NULL THEN N'PriceChannel' ELSE N'Public' END,
           COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m
                     WHERE m.BusinessId=p.BusinessId AND m.WarehouseId=@WarehouseId AND m.ProductId=p.ProductId),0),
           p.ManageStock
    FROM dbo.Products p
    CROSS APPLY(SELECT TOP(1) pp.Amount,pp.CostBasisAmount FROM dbo.ProductPrices pp
                WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId AND pp.IsActive=1
                  AND pp.ValidFrom<=SYSDATETIMEOFFSET() AND(pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET())
                ORDER BY pp.ValidFrom DESC)basePrice
    LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=@CustomerId
    LEFT JOIN dbo.PriceChannels channel ON channel.PriceChannelId=setting.PriceChannelId AND channel.BusinessId=@BusinessId AND channel.IsActive=1
    OUTER APPLY(SELECT COALESCE((SELECT NULLIF(balance.AverageUnitCost,0) FROM dbo.InventoryBalances balance WHERE balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId AND balance.ProductId=p.ProductId),basePrice.CostBasisAmount,0) Amount)cost
    OUTER APPLY(SELECT TOP(1)item.Amount FROM dbo.ResolvedPriceChannelItems item WHERE item.PriceChannelId=channel.PriceChannelId AND item.ProductId=p.ProductId AND item.MinimumQuantity<=1 AND item.IsActive=1 AND item.ValidFrom<=SYSDATETIMEOFFSET() AND(item.ValidUntil IS NULL OR item.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY item.MinimumQuantity DESC,item.ValidFrom DESC)special
    OUTER APPLY(SELECT CONVERT(decimal(19,4),ROUND(CASE channel.Strategy WHEN N'TieredProductPrice' THEN special.Amount WHEN N'PercentageOverBasePrice' THEN basePrice.Amount*(1+COALESCE(channel.Value,0)/100) WHEN N'PercentageOverAverageCost' THEN cost.Amount*(1+COALESCE(channel.Value,0)/100) WHEN N'FixedMarginOverAverageCost' THEN cost.Amount/(1-COALESCE(channel.Value,0)/100) WHEN N'SellAtAverageCost' THEN cost.Amount END,4)) Amount WHERE NOT EXISTS(SELECT 1 FROM dbo.PriceChannelExclusions e WHERE e.PriceChannelId=channel.PriceChannelId AND e.ProductId=p.ProductId))channelPrice
    WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND(@Search=N''
      OR p.Name COLLATE Latin1_General_100_CI_AI LIKE @Contains COLLATE Latin1_General_100_CI_AI
      OR p.ProductCode COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI
      OR p.Sku COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI
      OR p.Reference COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI)
    ORDER BY CASE WHEN p.ProductCode=@Search OR p.Sku=@Search THEN 0 ELSE 1 END,p.Name,p.ProductId
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
