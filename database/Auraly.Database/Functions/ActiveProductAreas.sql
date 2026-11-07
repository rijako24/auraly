CREATE FUNCTION [catalog].[ActiveProductAreas](@TenantId UNIQUEIDENTIFIER)
RETURNS TABLE
AS RETURN
(
    SELECT area.ProductCategoryId,area.Name,area.DisplayOrder,COUNT(*) ProductCount
    FROM dbo.Products product
    JOIN dbo.ProductCategories category
      ON category.ProductCategoryId=product.ProductCategoryId
     AND category.TenantId=@TenantId AND category.IsActive=1
    LEFT JOIN dbo.ProductCategories parent
      ON parent.ProductCategoryId=category.ParentProductCategoryId
     AND parent.TenantId=@TenantId AND parent.IsActive=1
    LEFT JOIN dbo.ProductCategories grandparent
      ON grandparent.ProductCategoryId=parent.ParentProductCategoryId
     AND grandparent.TenantId=@TenantId AND grandparent.IsActive=1
    LEFT JOIN dbo.ProductCategories greatgrandparent
      ON greatgrandparent.ProductCategoryId=grandparent.ParentProductCategoryId
     AND greatgrandparent.TenantId=@TenantId AND greatgrandparent.IsActive=1
    JOIN dbo.ProductCategories area
      ON area.ProductCategoryId=COALESCE(greatgrandparent.ProductCategoryId,
           grandparent.ProductCategoryId,parent.ProductCategoryId,category.ProductCategoryId)
     AND area.TenantId=@TenantId AND area.IsActive=1
     AND area.ParentProductCategoryId IS NULL
    WHERE product.TenantId=@TenantId AND product.IsActive=1 AND product.IsIngredient=0
    GROUP BY area.ProductCategoryId,area.Name,area.DisplayOrder
);
