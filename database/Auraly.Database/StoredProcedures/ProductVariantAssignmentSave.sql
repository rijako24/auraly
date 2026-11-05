CREATE PROCEDURE [catalog].[ProductVariantAssignmentSave]
    @TenantId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER,
    @ProductColorId UNIQUEIDENTIFIER = NULL,
    @ProductSizeId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE TenantId=@TenantId AND ProductId=@ProductId)
        THROW 51020, 'El producto no pertenece a la empresa.', 1;
    IF @ProductColorId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM catalog.ProductColors color
        JOIN dbo.Products product ON product.TenantId=color.TenantId AND product.ProductId=@ProductId
        WHERE color.TenantId=@TenantId AND color.ProductColorId=@ProductColorId
          AND (color.IsActive=1 OR product.ProductColorId=color.ProductColorId))
        THROW 51020, 'El color no está activo o no pertenece a la empresa.', 1;
    IF @ProductSizeId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM catalog.ProductSizes size
        JOIN dbo.Products product ON product.TenantId=size.TenantId AND product.ProductId=@ProductId
        WHERE size.TenantId=@TenantId AND size.ProductSizeId=@ProductSizeId
          AND (size.IsActive=1 OR product.ProductSizeId=size.ProductSizeId))
        THROW 51020, 'La talla no está activa o no pertenece a la empresa.', 1;
    UPDATE dbo.Products SET ProductColorId=@ProductColorId,ProductSizeId=@ProductSizeId
    WHERE TenantId=@TenantId AND ProductId=@ProductId;
END;
