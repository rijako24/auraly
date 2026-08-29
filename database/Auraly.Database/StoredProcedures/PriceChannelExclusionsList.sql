CREATE PROCEDURE [dbo].[PriceChannelExclusionsList]
    @Id UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.PriceChannels
        WHERE PriceChannelId = @Id AND BusinessId = @BusinessId)
    BEGIN
        THROW 51004, 'Price channel not found', 1;
    END

    ;WITH CategoryTree AS
    (
        SELECT category.ProductCategoryId, category.ParentProductCategoryId,
               category.Name, CONVERT(INT, 0) AS Depth,
               CONVERT(NVARCHAR(MAX), category.Name) AS [Path]
        FROM dbo.ProductCategories category
        WHERE category.BusinessId = @BusinessId
          AND category.ParentProductCategoryId IS NULL
        UNION ALL
        SELECT child.ProductCategoryId, child.ParentProductCategoryId,
               child.Name, parent.Depth + 1,
               CONVERT(NVARCHAR(MAX), CONCAT(parent.[Path], N' / ', child.Name))
        FROM dbo.ProductCategories child
        JOIN CategoryTree parent
          ON parent.ProductCategoryId = child.ParentProductCategoryId
        WHERE child.BusinessId = @BusinessId
    )
    SELECT exclusion.PriceChannelExclusionId,
           exclusion.ScopeType,
           COALESCE(exclusion.ProductId, exclusion.ProductCategoryId, exclusion.ProductBrandId) AS ScopeId,
           COALESCE(product.Name, category.[Path], brand.Name) AS ScopeName,
           category.Depth AS CategoryDepth,
           product.ProductCode
    FROM dbo.PriceChannelExclusions exclusion
    LEFT JOIN dbo.Products product
      ON product.ProductId = exclusion.ProductId
     AND product.TenantId = (SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
    LEFT JOIN CategoryTree category
      ON category.ProductCategoryId = exclusion.ProductCategoryId
    LEFT JOIN dbo.ProductBrands brand
      ON brand.ProductBrandId = exclusion.ProductBrandId AND brand.BusinessId = @BusinessId
    WHERE exclusion.PriceChannelId = @Id
    ORDER BY exclusion.ScopeType, ScopeName, exclusion.PriceChannelExclusionId;
END
