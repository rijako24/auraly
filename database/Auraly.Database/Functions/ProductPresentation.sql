CREATE FUNCTION [catalog].[ProductPresentation] (@TenantId UNIQUEIDENTIFIER, @ProductId UNIQUEIDENTIFIER)
RETURNS TABLE
AS RETURN
(
    SELECT image.MediaUrl AS ImageUrl,
        COALESCE(link.ParentProductId,CASE WHEN EXISTS(
            SELECT 1 FROM dbo.ProductLinks children
            WHERE children.TenantId=@TenantId AND children.ParentProductId=product.ProductId AND children.IsActive=1)
            THEN product.ProductId END) AS FamilyProductId,
        product.ProductColorId,color.Name AS ColorName,
        product.ProductSizeId,size.Name AS SizeName
    FROM dbo.Products product
    LEFT JOIN dbo.ProductLinks link ON link.TenantId=@TenantId AND link.ChildProductId=product.ProductId AND link.IsActive=1
    LEFT JOIN catalog.ProductColors color ON color.TenantId=@TenantId AND color.ProductColorId=product.ProductColorId
    LEFT JOIN catalog.ProductSizes size ON size.TenantId=@TenantId AND size.ProductSizeId=product.ProductSizeId
    OUTER APPLY(SELECT TOP(1) item.MediaUrl FROM dbo.ProductImages item
        WHERE item.TenantId=@TenantId AND item.ProductId=product.ProductId
          AND item.ProductOfferId IS NULL AND item.IsActive=1
        ORDER BY item.IsPrimary DESC,item.DisplayOrder,item.ProductImageId) image
    WHERE product.TenantId=@TenantId AND product.ProductId=@ProductId
);
