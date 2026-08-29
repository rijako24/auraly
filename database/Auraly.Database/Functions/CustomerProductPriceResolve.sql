CREATE FUNCTION [dbo].[CustomerProductPriceResolve]
(
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER,
    @Quantity DECIMAL(19,6),
    @At DATETIMEOFFSET(7)
)
RETURNS TABLE
AS
RETURN
(
    WITH ProductCategoryAncestors AS
    (
        SELECT category.ProductCategoryId, category.ParentProductCategoryId
        FROM dbo.Products scopedProduct
        JOIN dbo.ProductCategories category
          ON category.ProductCategoryId = scopedProduct.ProductCategoryId
         AND category.BusinessId = @BusinessId
        WHERE scopedProduct.TenantId = (SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
          AND scopedProduct.ProductId = @ProductId
        UNION ALL
        SELECT parent.ProductCategoryId, parent.ParentProductCategoryId
        FROM dbo.ProductCategories parent
        JOIN ProductCategoryAncestors child
          ON child.ParentProductCategoryId = parent.ProductCategoryId
        WHERE parent.BusinessId = @BusinessId
    )
    SELECT
        COALESCE(channelPrice.Amount, basePrice.Amount) AS Amount,
        basePrice.CurrencyCode,
        CASE WHEN channelPrice.Amount IS NOT NULL THEN N'PriceChannel' ELSE N'Base' END AS PriceSource,
        channel.PriceChannelId
    FROM dbo.Products product
    CROSS APPLY
    (
        SELECT TOP (1) price.Amount, price.CurrencyCode, price.CostBasisAmount,
               price.TargetMarginPercent, price.EffectiveMarginPercent
        FROM dbo.ProductPrices price
        WHERE price.BusinessId = @BusinessId
          AND price.ProductId = product.ProductId
          AND price.IsActive = 1
          AND price.ValidFrom <= @At
          AND (price.ValidUntil IS NULL OR price.ValidUntil > @At)
        ORDER BY price.ValidFrom DESC, price.ProductPriceId
    ) basePrice
    LEFT JOIN dbo.CustomerPricingSettings setting
      ON setting.CustomerId = @CustomerId
     AND EXISTS
     (
         SELECT 1
         FROM dbo.Customers customer
         WHERE customer.CustomerId = setting.CustomerId
           AND customer.BusinessId = @BusinessId
           AND customer.IsActive = 1
     )
     AND setting.ValidFrom <= @At
     AND (setting.ValidUntil IS NULL OR setting.ValidUntil > @At)
    LEFT JOIN dbo.PriceChannels channel
      ON channel.PriceChannelId = setting.PriceChannelId
     AND channel.BusinessId = @BusinessId
     AND channel.IsActive = 1
    OUTER APPLY
    (
        SELECT COALESCE(MAX(NULLIF(balance.AverageUnitCost, 0)), basePrice.CostBasisAmount, 0) AS Amount
        FROM dbo.InventoryBalances balance
        WHERE balance.BusinessId = @BusinessId
          AND balance.WarehouseId = @WarehouseId
          AND balance.ProductId = product.ProductId
    ) cost
    OUTER APPLY
    (
        SELECT COALESCE(
            (SELECT TOP (1) latest.LatestUnitCost
             FROM dbo.SupplierProductLatestCosts latest
             WHERE latest.BusinessId = @BusinessId
               AND latest.ProductId = product.ProductId
             ORDER BY latest.ObservedAt DESC, latest.SupplierId),
            basePrice.CostBasisAmount,
            cost.Amount,
            0) AS Amount
    ) latestCost
    OUTER APPLY
    (
        SELECT TOP (1) item.Amount
        FROM dbo.ResolvedPriceChannelItems item
        WHERE item.PriceChannelId = channel.PriceChannelId
          AND item.ProductId = product.ProductId
          AND item.MinimumQuantity <= @Quantity
          AND item.IsActive = 1
        ORDER BY item.MinimumQuantity DESC, item.CreatedAt DESC, item.ResolvedPriceChannelItemId
    ) special
    OUTER APPLY
    (
        SELECT dbo.PriceChannelAmountCalculate(
            channel.Strategy,
            channel.Value,
            basePrice.Amount,
            cost.Amount,
            latestCost.Amount,
            COALESCE(basePrice.TargetMarginPercent, basePrice.EffectiveMarginPercent),
            special.Amount) AS Amount
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.PriceChannelExclusions exclusion
            WHERE exclusion.PriceChannelId = channel.PriceChannelId
              AND
              (
                  exclusion.ProductId = product.ProductId
                  OR exclusion.ProductBrandId = product.ProductBrandId
                  OR exclusion.ProductCategoryId IN
                     (SELECT ancestor.ProductCategoryId FROM ProductCategoryAncestors ancestor)
              )
        )
          AND (channel.Strategy <> N'TieredProductPrice' OR special.Amount IS NOT NULL)
          AND dbo.PriceChannelAmountCalculate(
                channel.Strategy,
                channel.Value,
                basePrice.Amount,
                cost.Amount,
                latestCost.Amount,
                COALESCE(basePrice.TargetMarginPercent, basePrice.EffectiveMarginPercent),
                special.Amount) IS NOT NULL
    ) channelPrice
    WHERE product.TenantId = (SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
      AND product.ProductId = @ProductId
      AND product.IsActive = 1
);
