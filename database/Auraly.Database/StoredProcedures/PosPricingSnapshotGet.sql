CREATE PROCEDURE [dbo].[PosPricingSnapshotGet]
    @DeviceId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.EnrolledDevices deviceValue
        JOIN dbo.Businesses businessValue
          ON businessValue.BusinessId = @BusinessId
         AND businessValue.TenantId = deviceValue.TenantId
         AND businessValue.IsActive = 1
        WHERE deviceValue.DeviceId = @DeviceId
          AND deviceValue.TenantId = @TenantId
          AND deviceValue.IsActive = 1)
        THROW 51020, 'The device pricing scope is invalid.', 1;

    -- Channel configuration is transferred verbatim. No channel/product price
    -- matrix is calculated or persisted for POS synchronization.
    SELECT channelValue.PriceChannelId,channelValue.Code,channelValue.Name,
           channelValue.Strategy,channelValue.Value
    FROM dbo.PriceChannels channelValue
    WHERE channelValue.BusinessId = @BusinessId
      AND channelValue.IsActive = 1;

    SELECT item.PriceChannelId,item.ProductId,item.MinimumQuantity,item.Amount,item.CurrencyCode
    FROM dbo.PriceChannelItems item
    JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=item.PriceChannelId
    WHERE channelValue.BusinessId=@BusinessId
      AND channelValue.IsActive=1
      AND item.IsActive=1
    ORDER BY item.PriceChannelId,item.ProductId,item.MinimumQuantity;

    SELECT exclusion.PriceChannelId,exclusion.ScopeType,exclusion.ProductId,
           exclusion.ProductCategoryId,exclusion.ProductBrandId
    FROM dbo.PriceChannelExclusions exclusion
    JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=exclusion.PriceChannelId
    WHERE channelValue.BusinessId=@BusinessId
      AND channelValue.IsActive=1
    ORDER BY exclusion.PriceChannelId,exclusion.PriceChannelExclusionId;

    SELECT customer.CustomerId,
           COALESCE(party.NormalizedIdentification, party.Identification, N''),
           COALESCE(party.DisplayName, party.LegalName, party.Identification, N''),
           CASE WHEN setting.ValidFrom <= SYSDATETIMEOFFSET()
                     AND (setting.ValidUntil IS NULL OR setting.ValidUntil > SYSDATETIMEOFFSET())
                THEN setting.PriceChannelId END,
           customer.RequiresElectronicInvoice,
           customer.IsActive,
           COALESCE(taxProfile.AppliesWithholding,CONVERT(BIT,0)),
           COALESCE(taxProfile.Responsibilities,N'[]'),
           taxProfile.JurisdictionCode
    FROM dbo.Customers customer
    JOIN dbo.Parties party
      ON party.PartyId = customer.PartyId
     AND party.TenantId = @TenantId
    LEFT JOIN dbo.CustomerPricingSettings setting
      ON setting.CustomerId = customer.CustomerId
    LEFT JOIN dbo.CounterpartyTaxProfiles taxProfile
      ON taxProfile.BusinessId=customer.BusinessId
     AND taxProfile.CounterpartyId=customer.CustomerId
    WHERE customer.BusinessId = @BusinessId
      AND party.IsActive = 1;

    ;WITH CurrentRules AS
    (
        SELECT ruleValue.*,
               ROW_NUMBER() OVER(PARTITION BY ruleValue.RuleId ORDER BY ruleValue.Version DESC) AS rn
        FROM dbo.WithholdingRules ruleValue
        WHERE ruleValue.BusinessId=@BusinessId
    )
    SELECT RuleId,Version,Code,Name,Kind,Direction,Moment,BaseKind,
           ConceptCode,JurisdictionCode,Rate,MinimumBase,
           RequiredResponsibilities,EffectiveFrom,EffectiveTo,IsActive
    FROM CurrentRules
    WHERE rn=1 AND IsActive=1 AND Direction=N'Sale' AND Moment=N'Accrual'
    ORDER BY Kind,Code;

    SELECT warehouseValue.AllowNegativeStockSales
    FROM dbo.Warehouses warehouseValue
    WHERE warehouseValue.WarehouseId = @WarehouseId
      AND warehouseValue.BusinessId = @BusinessId
      AND warehouseValue.IsActive = 1;

    SELECT tenantValue.AllowPromotionChannelCombination
    FROM dbo.Tenants tenantValue
    WHERE tenantValue.TenantId = @TenantId
      AND tenantValue.IsActive = 1;

    SELECT promotion.PromotionId,promotion.Name,promotion.Priority,promotion.IsCombinable,
           promotion.CouponCode,promotion.StartsAtUtc,promotion.EndsAtUtc,promotion.CreatedAt,
           COALESCE((
             SELECT CONVERT(INT,conditionValue.ItemType) ItemType,conditionValue.ProductId,
                    conditionValue.ServiceId,conditionValue.CategoryName,
                    conditionValue.MinQuantity MinimumQuantity,conditionValue.MinSubtotal MinimumSubtotal
             FROM dbo.PromotionConditions conditionValue
             WHERE conditionValue.PromotionId=promotion.PromotionId
             ORDER BY conditionValue.PromotionConditionId
             FOR JSON PATH),N'[]') ConditionsJson,
           COALESCE((
             SELECT CONVERT(INT,benefit.BenefitType) BenefitType,
                    CONVERT(INT,benefit.TargetItemType) TargetItemType,benefit.ProductId,
                    benefit.ServiceId,benefit.CategoryName,benefit.DiscountPercentage,
                    benefit.DiscountAmount,benefit.FixedUnitPrice,benefit.AppliesToQuantity
             FROM dbo.PromotionBenefits benefit
             WHERE benefit.PromotionId=promotion.PromotionId
             ORDER BY benefit.PromotionBenefitId
             FOR JSON PATH),N'[]') BenefitsJson
    FROM dbo.Promotions promotion
    WHERE promotion.TenantId=@TenantId
      AND (promotion.AppliesToAllBusinesses=1
           OR EXISTS(SELECT 1 FROM pricing.PromotionBusinessScopes scope
                     WHERE scope.PromotionId=promotion.PromotionId AND scope.BusinessId=@BusinessId))
      AND promotion.IsActive=1
      AND (promotion.StartsAtUtc IS NULL OR promotion.StartsAtUtc<=SYSUTCDATETIME())
      AND (promotion.EndsAtUtc IS NULL OR promotion.EndsAtUtc>=SYSUTCDATETIME())
    ORDER BY promotion.Priority DESC,promotion.CreatedAt,promotion.PromotionId;
END;
