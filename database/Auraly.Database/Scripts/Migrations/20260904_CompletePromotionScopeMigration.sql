IF OBJECT_ID(N'dbo.PromotionBusinessScopeMigration', N'U') IS NOT NULL
   AND OBJECT_ID(N'pricing.PromotionBusinessScopes', N'U') IS NOT NULL
BEGIN
    INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
    SELECT migration.PromotionId,migration.BusinessId,migration.TenantId
    FROM dbo.PromotionBusinessScopeMigration migration
    WHERE NOT EXISTS(
        SELECT 1
        FROM pricing.PromotionBusinessScopes scopeValue
        WHERE scopeValue.PromotionId=migration.PromotionId
          AND scopeValue.BusinessId=migration.BusinessId);

    DROP TABLE dbo.PromotionBusinessScopeMigration;
END;
