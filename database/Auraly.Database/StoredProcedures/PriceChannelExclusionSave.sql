CREATE PROCEDURE [dbo].[PriceChannelExclusionSave]
    @ExclusionId UNIQUEIDENTIFIER,
    @Id UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @ScopeType NVARCHAR(16),
    @ScopeId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF @ScopeType NOT IN (N'Product', N'Category', N'Brand')
    BEGIN
        THROW 51005, 'Invalid exclusion scope', 1;
    END

    IF NOT EXISTS (
        SELECT 1 FROM dbo.PriceChannels
        WHERE PriceChannelId = @Id AND BusinessId = @BusinessId)
    BEGIN
        THROW 51004, 'Price channel not found', 1;
    END

    IF @ScopeType = N'Product'
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM dbo.Products
            WHERE ProductId = @ScopeId AND BusinessId = @BusinessId AND IsActive = 1)
        BEGIN
            THROW 51004, 'Product not found', 1;
        END
        INSERT dbo.PriceChannelExclusions(
            PriceChannelExclusionId, PriceChannelId, ScopeType, ProductId, CreatedAt)
        VALUES(@ExclusionId, @Id, @ScopeType, @ScopeId, SYSDATETIMEOFFSET());
        RETURN;
    END

    IF @ScopeType = N'Category'
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM dbo.ProductCategories
            WHERE ProductCategoryId = @ScopeId AND BusinessId = @BusinessId AND IsActive = 1)
        BEGIN
            THROW 51004, 'Product category not found', 1;
        END
        INSERT dbo.PriceChannelExclusions(
            PriceChannelExclusionId, PriceChannelId, ScopeType, ProductCategoryId, CreatedAt)
        VALUES(@ExclusionId, @Id, @ScopeType, @ScopeId, SYSDATETIMEOFFSET());
        RETURN;
    END

    IF @ScopeType = N'Brand'
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM dbo.ProductBrands
            WHERE ProductBrandId = @ScopeId AND BusinessId = @BusinessId AND IsActive = 1)
        BEGIN
            THROW 51004, 'Product brand not found', 1;
        END
        INSERT dbo.PriceChannelExclusions(
            PriceChannelExclusionId, PriceChannelId, ScopeType, ProductBrandId, CreatedAt)
        VALUES(@ExclusionId, @Id, @ScopeType, @ScopeId, SYSDATETIMEOFFSET());
        RETURN;
    END
END
