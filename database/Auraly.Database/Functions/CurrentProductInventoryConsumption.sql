CREATE FUNCTION [inventory].[CurrentProductConsumption]
(
    @TenantId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER
)
RETURNS TABLE
AS
RETURN
(
    SELECT consumption.*
    FROM dbo.Products product
    OUTER APPLY
    (
        SELECT TOP(1) versionRow.ProductRecipeVersionId,versionRow.IsEnabled
        FROM catalog.ProductCompositionVersions versionRow
        WHERE versionRow.TenantId=@TenantId AND versionRow.ProductId=@ProductId
        ORDER BY versionRow.[Version] DESC
    ) latest
    CROSS APPLY inventory.ProductConsumption(@TenantId,@ProductId,product.ManageStock,
        CASE WHEN latest.IsEnabled=1 THEN latest.ProductRecipeVersionId END) consumption
    WHERE product.TenantId=@TenantId AND product.ProductId=@ProductId
      AND (product.ManageStock=1 OR (product.HasIngredients=0 AND product.IsCombo=0) OR latest.IsEnabled=1)
);
