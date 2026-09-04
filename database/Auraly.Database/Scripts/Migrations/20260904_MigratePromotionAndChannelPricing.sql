SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- PriceChannelItems is the configured tier table. Preserve existing configured
-- rows while removing the old name that implied a materialized product matrix.
IF OBJECT_ID(N'dbo.ResolvedPriceChannelItems', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PriceChannelItems', N'U') IS NULL
BEGIN
    EXEC sys.sp_rename N'dbo.ResolvedPriceChannelItems', N'PriceChannelItems';

    IF COL_LENGTH(N'dbo.PriceChannelItems', N'ResolvedPriceChannelItemId') IS NOT NULL
        EXEC sys.sp_rename
            N'dbo.PriceChannelItems.ResolvedPriceChannelItemId',
            N'PriceChannelItemId',
            N'COLUMN';
END;

-- Catalog synchronization sessions are short-lived cursors. Existing sessions
-- predate warehouse scoping and cannot be assigned safely, so expire them and
-- require clients to start a fresh, correctly scoped synchronization session.
IF OBJECT_ID(N'dbo.CatalogSyncSessions', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.CatalogSyncSessions', N'WarehouseId') IS NULL
BEGIN
    DELETE FROM dbo.CatalogSyncSessions;
    ALTER TABLE dbo.CatalogSyncSessions ADD WarehouseId UNIQUEIDENTIFIER NULL;
    ALTER TABLE dbo.CatalogSyncSessions ALTER COLUMN WarehouseId UNIQUEIDENTIFIER NOT NULL;
END;

IF SCHEMA_ID(N'pricing') IS NULL
    EXEC(N'CREATE SCHEMA pricing AUTHORIZATION dbo;');

IF OBJECT_ID(N'dbo.Promotions', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Promotions', N'TenantId') IS NULL
        ALTER TABLE dbo.Promotions ADD TenantId UNIQUEIDENTIFIER NULL;

    EXEC sys.sp_executesql N'
        UPDATE promotion
        SET TenantId = business.TenantId
        FROM dbo.Promotions promotion
        INNER JOIN dbo.Businesses business
            ON business.BusinessId = promotion.BusinessId
        WHERE promotion.TenantId IS NULL;';

    DECLARE @UnscopedPromotions BIGINT;
    EXEC sys.sp_executesql
        N'SELECT @Count=COUNT_BIG(*) FROM dbo.Promotions WHERE TenantId IS NULL;',
        N'@Count BIGINT OUTPUT',
        @Count=@UnscopedPromotions OUTPUT;
    IF @UnscopedPromotions > 0
        THROW 51710, 'Every existing promotion must resolve to its business tenant.', 1;

    IF COL_LENGTH(N'dbo.Promotions', N'AppliesToAllBusinesses') IS NULL
        ALTER TABLE dbo.Promotions
            ADD AppliesToAllBusinesses BIT NOT NULL
                CONSTRAINT DF_Promotions_AppliesToAllBusinesses DEFAULT (0);

    IF OBJECT_ID(N'pricing.PromotionBusinessScopes', N'U') IS NULL
    BEGIN
        CREATE TABLE pricing.PromotionBusinessScopes
        (
            PromotionId UNIQUEIDENTIFIER NOT NULL,
            BusinessId UNIQUEIDENTIFIER NOT NULL,
            TenantId UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT PK_PromotionBusinessScopes
                PRIMARY KEY (PromotionId, BusinessId)
        );
    END;

    EXEC sys.sp_executesql N'
        INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
        SELECT promotion.PromotionId,promotion.BusinessId,promotion.TenantId
        FROM dbo.Promotions promotion
        WHERE NOT EXISTS(
            SELECT 1
            FROM pricing.PromotionBusinessScopes scopeValue
            WHERE scopeValue.PromotionId=promotion.PromotionId
              AND scopeValue.BusinessId=promotion.BusinessId);';

    IF COL_LENGTH(N'dbo.PromotionConditions', N'TenantId') IS NULL
        ALTER TABLE dbo.PromotionConditions ADD TenantId UNIQUEIDENTIFIER NULL;

    EXEC sys.sp_executesql N'
        IF EXISTS(
            SELECT 1
            FROM dbo.PromotionConditions conditionValue
            INNER JOIN dbo.Promotions promotion
                ON promotion.PromotionId=conditionValue.PromotionId
            INNER JOIN dbo.Businesses business
                ON business.BusinessId=conditionValue.BusinessId
            WHERE business.TenantId<>promotion.TenantId)
            THROW 51711,''A promotion condition belongs to a different tenant than its promotion.'',1;

        UPDATE conditionValue
        SET TenantId=promotion.TenantId
        FROM dbo.PromotionConditions conditionValue
        INNER JOIN dbo.Promotions promotion
            ON promotion.PromotionId=conditionValue.PromotionId
        WHERE conditionValue.TenantId IS NULL;';

    IF COL_LENGTH(N'dbo.PromotionBenefits', N'TenantId') IS NULL
        ALTER TABLE dbo.PromotionBenefits ADD TenantId UNIQUEIDENTIFIER NULL;

    EXEC sys.sp_executesql N'
        IF EXISTS(
            SELECT 1
            FROM dbo.PromotionBenefits benefit
            INNER JOIN dbo.Promotions promotion
                ON promotion.PromotionId=benefit.PromotionId
            INNER JOIN dbo.Businesses business
                ON business.BusinessId=benefit.BusinessId
            WHERE business.TenantId<>promotion.TenantId)
            THROW 51712,''A promotion benefit belongs to a different tenant than its promotion.'',1;

        UPDATE benefit
        SET TenantId=promotion.TenantId
        FROM dbo.PromotionBenefits benefit
        INNER JOIN dbo.Promotions promotion
            ON promotion.PromotionId=benefit.PromotionId
        WHERE benefit.TenantId IS NULL;';

    IF COL_LENGTH(N'dbo.PromotionApplications', N'TenantId') IS NULL
        ALTER TABLE dbo.PromotionApplications ADD TenantId UNIQUEIDENTIFIER NULL;

    EXEC sys.sp_executesql N'
        IF EXISTS(
            SELECT 1
            FROM dbo.PromotionApplications applicationValue
            INNER JOIN dbo.Promotions promotion
                ON promotion.PromotionId=applicationValue.PromotionId
            INNER JOIN dbo.Businesses business
                ON business.BusinessId=applicationValue.BusinessId
            WHERE business.TenantId<>promotion.TenantId)
            THROW 51713,''A promotion application belongs to a different tenant than its promotion.'',1;

        UPDATE applicationValue
        SET TenantId=promotion.TenantId
        FROM dbo.PromotionApplications applicationValue
        INNER JOIN dbo.Promotions promotion
            ON promotion.PromotionId=applicationValue.PromotionId
        WHERE applicationValue.TenantId IS NULL;';

    DECLARE @UnscopedPromotionChildren BIGINT;
    EXEC sys.sp_executesql N'
        SELECT @Count=
            (SELECT COUNT_BIG(*) FROM dbo.PromotionConditions WHERE TenantId IS NULL) +
            (SELECT COUNT_BIG(*) FROM dbo.PromotionBenefits WHERE TenantId IS NULL) +
            (SELECT COUNT_BIG(*) FROM dbo.PromotionApplications WHERE TenantId IS NULL);',
        N'@Count BIGINT OUTPUT',
        @Count=@UnscopedPromotionChildren OUTPUT;
    IF @UnscopedPromotionChildren > 0
        THROW 51714, 'Every promotion child row must resolve to its promotion tenant.', 1;

    ALTER TABLE dbo.Promotions ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
    ALTER TABLE dbo.PromotionConditions ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
    ALTER TABLE dbo.PromotionBenefits ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
    ALTER TABLE dbo.PromotionApplications ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
END;

COMMIT TRANSACTION;
