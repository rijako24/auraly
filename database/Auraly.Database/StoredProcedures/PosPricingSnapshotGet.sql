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

    ;WITH CategoryAncestors AS
    (
        SELECT category.ProductCategoryId AS DescendantId,
               category.ProductCategoryId AS AncestorId,
               category.ParentProductCategoryId
        FROM dbo.ProductCategories category
        WHERE category.BusinessId = @BusinessId
        UNION ALL
        SELECT child.DescendantId, parent.ProductCategoryId, parent.ParentProductCategoryId
        FROM CategoryAncestors child
        JOIN dbo.ProductCategories parent
          ON parent.ProductCategoryId = child.ParentProductCategoryId
         AND parent.BusinessId = @BusinessId
    )
    SELECT channelValue.PriceChannelId,
           product.ProductId,
           CASE WHEN channelValue.Strategy = N'TieredProductPrice'
                THEN special.MinimumQuantity ELSE CONVERT(DECIMAL(19,6), 1) END,
           calculated.Amount,
           basePrice.CurrencyCode,
           CONVERT(BIT, 0)
    FROM dbo.PriceChannels channelValue
    JOIN dbo.Products product
      ON product.TenantId = @TenantId
     AND product.IsActive = 1
    CROSS APPLY
    (
        SELECT TOP (1) price.Amount,
               price.CurrencyCode,
               price.CostBasisAmount,
               price.TargetMarginPercent,
               price.EffectiveMarginPercent
        FROM dbo.ProductPrices price
        WHERE price.BusinessId = @BusinessId
          AND price.ProductId = product.ProductId
          AND price.IsActive = 1
          AND price.ValidFrom <= SYSDATETIMEOFFSET()
          AND (price.ValidUntil IS NULL OR price.ValidUntil > SYSDATETIMEOFFSET())
        ORDER BY price.ValidFrom DESC, price.ProductPriceId
    ) basePrice
    OUTER APPLY
    (
        SELECT COALESCE(
            MAX(NULLIF(balance.AverageUnitCost, 0)),
            basePrice.CostBasisAmount,
            0) AS Amount
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
        SELECT item.Amount, item.MinimumQuantity
        FROM dbo.ResolvedPriceChannelItems item
        WHERE item.PriceChannelId = channelValue.PriceChannelId
          AND item.ProductId = product.ProductId
          AND item.IsActive = 1
          AND channelValue.Strategy = N'TieredProductPrice'
    ) special
    CROSS APPLY
    (
        SELECT dbo.PriceChannelAmountCalculate(
            channelValue.Strategy,
            channelValue.Value,
            basePrice.Amount,
            cost.Amount,
            latestCost.Amount,
            COALESCE(basePrice.TargetMarginPercent, basePrice.EffectiveMarginPercent),
            special.Amount) AS Amount
    ) calculated
    WHERE channelValue.BusinessId = @BusinessId
      AND channelValue.IsActive = 1
      AND calculated.Amount IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.PriceChannelExclusions exclusion
          WHERE exclusion.PriceChannelId = channelValue.PriceChannelId
            AND
            (
                exclusion.ProductId = product.ProductId
                OR exclusion.ProductBrandId = product.ProductBrandId
                OR exclusion.ProductCategoryId IN
                (
                    SELECT ancestor.AncestorId
                    FROM CategoryAncestors ancestor
                    WHERE ancestor.DescendantId = product.ProductCategoryId
                )
            )
      );

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
END;
