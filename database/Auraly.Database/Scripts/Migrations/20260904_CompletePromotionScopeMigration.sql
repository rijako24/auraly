IF OBJECT_ID(N'dbo.PromotionBusinessScopeMigration', N'U') IS NOT NULL
   AND OBJECT_ID(N'pricing.PromotionBusinessScopes', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
        SELECT migration.PromotionId,migration.BusinessId,migration.TenantId
        FROM dbo.PromotionBusinessScopeMigration migration
        WHERE NOT EXISTS(
            SELECT 1
            FROM pricing.PromotionBusinessScopes scopeValue
            WHERE scopeValue.PromotionId=migration.PromotionId
              AND scopeValue.BusinessId=migration.BusinessId);

        DROP TABLE dbo.PromotionBusinessScopeMigration;';
END;

IF OBJECT_ID(N'dbo.PriceChannelItemMigration', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PriceChannelItems', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        INSERT dbo.PriceChannelItems
          (PriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,
           CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt)
        SELECT migration.PriceChannelItemId,migration.PriceChannelId,
               migration.ProductId,migration.MinimumQuantity,migration.Amount,
               migration.CurrencyCode,migration.ValidFrom,migration.ValidUntil,
               migration.IsActive,migration.CreatedAt
        FROM dbo.PriceChannelItemMigration migration
        WHERE NOT EXISTS(
            SELECT 1 FROM dbo.PriceChannelItems item
            WHERE item.PriceChannelItemId=migration.PriceChannelItemId);

        DROP TABLE dbo.PriceChannelItemMigration;

        IF OBJECT_ID(N''dbo.ResolvedPriceChannelItems'', N''U'') IS NOT NULL
            DROP TABLE dbo.ResolvedPriceChannelItems;';
END;
