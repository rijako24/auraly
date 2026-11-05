CREATE FUNCTION [dbo].[ProductPriceCostBasis](
    @TenantId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@ProductId UNIQUEIDENTIFIER,@AsOf DATETIMEOFFSET(7))
RETURNS TABLE
AS RETURN
(
    -- SharesPrice is the existing one-level cost link; compositions own their cost.
    SELECT costSource.CostBasisAmount,
      CASE WHEN product.HasIngredients=1 OR product.IsCombo=1 THEN N'Composition'
        WHEN link.ProductLinkId IS NOT NULL THEN N'LinkedProduct' ELSE resolved.Origin END CostBasisType,
      costSource.IsGross,
      tax.Rate PurchaseTaxRate,
      CASE WHEN costSource.CostBasisAmount>0 THEN
        CASE WHEN costSource.IsGross=1 THEN costSource.CostBasisAmount
          ELSE costSource.CostBasisAmount*(1+tax.Rate/100) END END UnitCostWithTax
    FROM dbo.Products product
    JOIN dbo.Businesses businessValue ON businessValue.BusinessId=@BusinessId AND businessValue.TenantId=@TenantId
    LEFT JOIN dbo.ProductLinks link ON link.TenantId=@TenantId AND link.ChildProductId=product.ProductId
      AND link.SharesPrice=1 AND link.IsActive=1
    JOIN dbo.Products basis ON basis.TenantId=@TenantId AND basis.ProductId=COALESCE(link.ParentProductId,product.ProductId)
    LEFT JOIN dbo.TaxProfiles tax ON tax.TenantId=@TenantId AND tax.TaxProfileId=product.PurchaseTaxProfileId AND tax.IsActive=1
    OUTER APPLY(SELECT TOP(1) value.CostBasisAmount,value.CostBasisType
      FROM dbo.ProductPricePreparations value WHERE value.BusinessId=@BusinessId AND value.ProductId=basis.ProductId
        AND value.Status=N'Pending' ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC) prepared
    OUTER APPLY(SELECT TOP(1) value.CostBasisAmount,value.CostBasisType
      FROM dbo.ProductPrices value WHERE value.BusinessId=@BusinessId AND value.ProductId=basis.ProductId
        AND (value.IsActive=1 OR value.ValidUntil IS NOT NULL)
        AND value.ValidFrom<=@AsOf AND (value.ValidUntil IS NULL OR value.ValidUntil>@AsOf)
      ORDER BY value.ValidFrom DESC,value.ProductPriceId) published
    OUTER APPLY(SELECT TOP(1) value.LatestUnitCost
      FROM dbo.SupplierProductLatestCosts value
      LEFT JOIN dbo.SupplierProducts association ON association.TenantId=@TenantId
        AND association.SupplierId=value.SupplierId AND association.ProductId=value.ProductId
      WHERE value.ProductId=basis.ProductId AND
        ((businessValue.SharesProductPrices=0 AND value.BusinessId=@BusinessId)
          OR (businessValue.SharesProductPrices=1 AND EXISTS(SELECT 1 FROM dbo.Businesses shared
            WHERE shared.BusinessId=value.BusinessId AND shared.TenantId=@TenantId AND shared.SharesProductPrices=1 AND shared.IsActive=1)))
      ORDER BY CASE WHEN association.IsPrimary=1 AND association.IsActive=1 THEN 0 ELSE 1 END,
        value.ObservedAt DESC,value.SupplierId) latest
    CROSS APPLY(SELECT
      CASE WHEN basis.HasIngredients=1 OR basis.IsCombo=1 THEN COALESCE(prepared.CostBasisAmount,published.CostBasisAmount)
        ELSE COALESCE(prepared.CostBasisAmount,latest.LatestUnitCost,published.CostBasisAmount) END Amount,
      CASE WHEN basis.HasIngredients=1 OR basis.IsCombo=1 THEN N'Composition'
        WHEN prepared.CostBasisAmount IS NOT NULL THEN prepared.CostBasisType
        WHEN latest.LatestUnitCost IS NOT NULL THEN N'ObservedSupplierCost' ELSE published.CostBasisType END Origin) resolved
    CROSS APPLY(SELECT TRY_CONVERT(DECIMAL(19,6),ROUND(resolved.Amount*COALESCE(link.PriceFactor,1),6)) CostBasisAmount,
      CONVERT(BIT,CASE WHEN basis.PurchaseTaxTreatment=N'CapitalizedCost'
        OR resolved.Origin=N'Composition' THEN 1 ELSE 0 END) IsGross) costSource
    WHERE product.TenantId=@TenantId AND product.ProductId=@ProductId
);
GO
