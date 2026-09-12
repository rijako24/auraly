CREATE PROCEDURE [dbo].[OnlineSalesProductSearch]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @WorkSessionId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER = NULL,
    @Search NVARCHAR(250),
    @Contains NVARCHAR(252),
    @Prefix NVARCHAR(251),
    @Skip INT,
    @Take INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT b.BusinessId,w.WarehouseId,s.WorkSessionId,w.AllowNegativeStockSales
    FROM dbo.Businesses b
    JOIN dbo.Warehouses w
      ON w.WarehouseId=@WarehouseId AND w.BusinessId=b.BusinessId
     AND w.IsActive=1 AND w.UseForSales=1
    JOIN dbo.WorkSessions s
      ON s.WorkSessionId=@WorkSessionId AND s.BusinessId=b.BusinessId
     AND s.TenantId=@TenantId AND s.UserId=@UserId AND s.Status=N'Open'
    WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId AND b.IsActive=1;

    DECLARE @Candidates TABLE(
      SortOrder INT NOT NULL PRIMARY KEY,ProductId UNIQUEIDENTIFIER NOT NULL,
      ProductCode NVARCHAR(64) NOT NULL,Reference NVARCHAR(120) NULL,Name NVARCHAR(250) NOT NULL,
      UnitCode NVARCHAR(24) NOT NULL,TaxCode NVARCHAR(16) NOT NULL,TaxRate DECIMAL(9,4) NOT NULL,
      UnitPrice DECIMAL(19,4) NOT NULL,CurrencyCode NVARCHAR(3) NOT NULL,
      IsActive BIT NOT NULL,IsWeighable BIT NOT NULL,AllowsFractionalSale BIT NOT NULL,
      CategoryName NVARCHAR(150) NULL,ProductCategoryId UNIQUEIDENTIFIER NULL,
      ProductBrandId UNIQUEIDENTIFIER NULL,AverageUnitCost DECIMAL(19,6) NOT NULL,
      LatestUnitCost DECIMAL(19,6) NOT NULL,TargetMarginPercent DECIMAL(9,6) NULL);

    ;WITH Ranked AS(
      SELECT ROW_NUMBER() OVER(ORDER BY CASE
               WHEN p.ProductCode=@Search OR p.Sku=@Search OR p.Reference=@Search OR EXISTS(
                 SELECT 1 FROM dbo.ProductBarcodes exactBarcode
                 WHERE exactBarcode.ProductId=p.ProductId AND exactBarcode.BusinessId=@BusinessId
                   AND exactBarcode.IsActive=1 AND exactBarcode.Barcode=@Search) THEN 0 ELSE 1 END,
               p.Name,p.ProductId) SortOrder,
             p.ProductId,COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N'') ProductCode,
             p.Reference,p.Name,COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA') UnitCode,
             COALESCE(t.DianTaxCode,N'01') TaxCode,COALESCE(t.Rate,0) TaxRate,
             price.Amount UnitPrice,price.CurrencyCode,p.IsActive,p.IsWeighable,p.AllowsFractionalSale,
             p.CategoryName,p.ProductCategoryId,p.ProductBrandId,
             COALESCE(NULLIF(balance.AverageUnitCost,0),price.CostBasisAmount,0) AverageUnitCost,
             COALESCE(latest.LatestUnitCost,price.CostBasisAmount,NULLIF(balance.AverageUnitCost,0),0) LatestUnitCost,
             COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent) TargetMarginPercent
      FROM dbo.Products p
      LEFT JOIN dbo.TaxProfiles t
        ON t.TaxProfileId=p.TaxProfileId AND t.BusinessId=@BusinessId AND t.IsActive=1
      CROSS APPLY(SELECT TOP(1) pp.Amount,pp.CurrencyCode,pp.CostBasisAmount,
                   pp.TargetMarginPercent,pp.EffectiveMarginPercent
        FROM dbo.ProductPrices pp
        WHERE pp.BusinessId=@BusinessId AND pp.ProductId=p.ProductId AND pp.IsActive=1
          AND pp.ValidFrom<=SYSDATETIMEOFFSET()
          AND(pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET())
        ORDER BY pp.ValidFrom DESC,pp.ProductPriceId) price
      LEFT JOIN dbo.InventoryBalances balance
        ON balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId
       AND balance.ProductId=p.ProductId
      OUTER APPLY(SELECT TOP(1) cost.LatestUnitCost
        FROM dbo.SupplierProductLatestCosts cost
        WHERE cost.BusinessId=@BusinessId AND cost.ProductId=p.ProductId
        ORDER BY cost.ObservedAt DESC,cost.SupplierId) latest
      WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.IsActive=1
        AND(@Search=N'' OR p.Name LIKE @Contains OR p.ProductCode LIKE @Prefix
          OR p.Sku LIKE @Prefix OR p.Reference LIKE @Prefix
          OR EXISTS(SELECT 1 FROM dbo.ProductBarcodes barcode
            WHERE barcode.ProductId=p.ProductId AND barcode.BusinessId=@BusinessId
              AND barcode.IsActive=1 AND barcode.Barcode LIKE @Prefix)
          OR EXISTS(SELECT 1 FROM dbo.ProductIdentifiers identifier
            WHERE identifier.ProductId=p.ProductId AND identifier.BusinessId=@BusinessId
              AND identifier.IsActive=1 AND identifier.Value LIKE @Prefix)))
    INSERT @Candidates
    SELECT SortOrder,ProductId,ProductCode,Reference,Name,UnitCode,TaxCode,TaxRate,
           UnitPrice,CurrencyCode,IsActive,IsWeighable,AllowsFractionalSale,CategoryName,
           ProductCategoryId,ProductBrandId,AverageUnitCost,LatestUnitCost,TargetMarginPercent
    FROM Ranked WHERE SortOrder>@Skip AND SortOrder<=@Skip+@Take
    ORDER BY SortOrder;

    ;WITH Ancestors AS(
      SELECT candidate.ProductId RootProductId,category.ProductCategoryId,category.ParentProductCategoryId
      FROM @Candidates candidate
      JOIN dbo.ProductCategories category ON category.ProductCategoryId=candidate.ProductCategoryId
      WHERE category.BusinessId=@BusinessId
      UNION ALL
      SELECT child.RootProductId,parent.ProductCategoryId,parent.ParentProductCategoryId
      FROM Ancestors child JOIN dbo.ProductCategories parent
        ON parent.ProductCategoryId=child.ParentProductCategoryId
      WHERE parent.BusinessId=@BusinessId)
    SELECT candidate.*,
           COALESCE((SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),ancestor.ProductCategoryId),N',')
             FROM Ancestors ancestor WHERE ancestor.RootProductId=candidate.ProductId),N'') AncestorIds
    FROM @Candidates candidate ORDER BY candidate.SortOrder;

    DECLARE @SelectedPriceChannelId UNIQUEIDENTIFIER;
    SELECT @SelectedPriceChannelId=CASE
      WHEN(setting.ValidFrom IS NULL OR setting.ValidFrom<=SYSDATETIMEOFFSET())
       AND(setting.ValidUntil IS NULL OR setting.ValidUntil>SYSDATETIMEOFFSET())
      THEN setting.PriceChannelId END
    FROM dbo.Customers customer
    LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=customer.CustomerId
    WHERE customer.CustomerId=@CustomerId AND customer.BusinessId=@BusinessId AND customer.IsActive=1;
    SELECT @SelectedPriceChannelId;

    SELECT PriceChannelId,Strategy,Value FROM dbo.PriceChannels
    WHERE BusinessId=@BusinessId AND IsActive=1 AND PriceChannelId=@SelectedPriceChannelId;

    SELECT item.PriceChannelId,item.ProductId,item.MinimumQuantity,item.Amount,item.CurrencyCode
    FROM dbo.PriceChannelItems item
    JOIN @Candidates candidate ON candidate.ProductId=item.ProductId
    JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=item.PriceChannelId
    WHERE channelValue.BusinessId=@BusinessId AND channelValue.IsActive=1 AND item.IsActive=1
      AND item.PriceChannelId=@SelectedPriceChannelId;

    SELECT exclusion.PriceChannelId,exclusion.ProductId,
           exclusion.ProductCategoryId,exclusion.ProductBrandId
    FROM dbo.PriceChannelExclusions exclusion
    JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=exclusion.PriceChannelId
    WHERE channelValue.BusinessId=@BusinessId AND channelValue.IsActive=1
      AND exclusion.PriceChannelId=@SelectedPriceChannelId
      AND(exclusion.ProductId IS NULL OR EXISTS(
        SELECT 1 FROM @Candidates candidate WHERE candidate.ProductId=exclusion.ProductId));

    SELECT tenant.AllowPromotionChannelCombination
    FROM dbo.Businesses business JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId
    WHERE business.BusinessId=@BusinessId;

    SELECT promotion.PromotionId,promotion.Name,promotion.Priority,promotion.IsCombinable,
           promotion.CouponCode,promotion.CreatedAt,
           COALESCE((SELECT CONVERT(INT,c.ItemType) ItemType,c.ProductId,c.ServiceId,
             c.ProductCategoryId,c.ServiceCategoryId,c.MinQuantity MinimumQuantity,c.MinSubtotal MinimumSubtotal
             FROM dbo.PromotionConditions c WHERE c.PromotionId=promotion.PromotionId
             ORDER BY c.PromotionConditionId FOR JSON PATH),N'[]'),
           COALESCE((SELECT CONVERT(INT,b.BenefitType) BenefitType,CONVERT(INT,b.TargetItemType) TargetItemType,
             b.ProductId,b.ServiceId,b.ProductCategoryId,b.ServiceCategoryId,b.DiscountPercentage,
             b.DiscountAmount,b.FixedUnitPrice,b.AppliesToQuantity
             FROM dbo.PromotionBenefits b WHERE b.PromotionId=promotion.PromotionId
             ORDER BY b.PromotionBenefitId FOR JSON PATH),N'[]')
    FROM dbo.Promotions promotion
    JOIN dbo.Businesses targetBusiness ON targetBusiness.BusinessId=@BusinessId
    WHERE promotion.TenantId=targetBusiness.TenantId
      AND(promotion.AppliesToAllBusinesses=1 OR EXISTS(
        SELECT 1 FROM pricing.PromotionBusinessScopes scope
        WHERE scope.PromotionId=promotion.PromotionId AND scope.BusinessId=@BusinessId))
      AND promotion.IsActive=1
      AND(promotion.StartsAtUtc IS NULL OR promotion.StartsAtUtc<=SYSUTCDATETIME())
      AND(promotion.EndsAtUtc IS NULL OR promotion.EndsAtUtc>=SYSUTCDATETIME())
    ORDER BY promotion.Priority DESC,promotion.CreatedAt,promotion.PromotionId;
END
