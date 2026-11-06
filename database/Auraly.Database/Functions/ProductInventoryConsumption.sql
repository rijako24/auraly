CREATE FUNCTION [inventory].[ProductConsumption]
(
    @TenantId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER,
    @ManageStock BIT,
    @ProductRecipeVersionId UNIQUEIDENTIFIER
)
RETURNS TABLE
AS
RETURN
(
    SELECT COALESCE(link.ParentProductId,product.ProductId) InventoryProductId,
           COALESCE(link.InventoryFactor,CONVERT(DECIMAL(19,6),1)) InventoryFactor,
           inventoryProduct.ManageStock,CAST(0 AS BIT) IsComponent,0 ComponentLineNumber
    FROM dbo.Products product
    LEFT JOIN dbo.ProductLinks link
      ON link.TenantId=@TenantId AND link.ChildProductId=product.ProductId
     AND link.SharesInventory=1 AND link.IsActive=1
    JOIN dbo.Products inventoryProduct
      ON inventoryProduct.TenantId=@TenantId
     AND inventoryProduct.ProductId=COALESCE(link.ParentProductId,product.ProductId)
    WHERE product.TenantId=@TenantId AND product.ProductId=@ProductId
      AND (@ManageStock=1 OR @ProductRecipeVersionId IS NULL)
    UNION ALL
    SELECT member.IngredientProductId,member.QuantityPerUnit,
           component.ManageStock,CAST(1 AS BIT),member.LineNumber
    FROM catalog.ProductCompositionVersions versionRow
    JOIN catalog.ProductCompositionComponents member
      ON member.TenantId=versionRow.TenantId
     AND member.ProductRecipeVersionId=versionRow.ProductRecipeVersionId
    JOIN dbo.Products component
      ON component.TenantId=member.TenantId AND component.ProductId=member.IngredientProductId
    WHERE versionRow.TenantId=@TenantId AND versionRow.ProductId=@ProductId
      AND versionRow.ProductRecipeVersionId=@ProductRecipeVersionId AND @ManageStock=0
);
