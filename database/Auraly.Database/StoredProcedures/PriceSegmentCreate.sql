CREATE PROCEDURE [dbo].[PriceSegmentCreate] @Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@Code NVARCHAR(32),@Name NVARCHAR(120),@Strategy NVARCHAR(48),@Value DECIMAL(19,6)=NULL,@ItemsJson NVARCHAR(MAX)=N'[]',@ExclusionsJson NVARCHAR(MAX)=N'[]' AS
BEGIN SET NOCOUNT ON;
 DECLARE @TenantId UNIQUEIDENTIFIER=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
 IF @TenantId IS NULL THROW 51004,'Business not found',1;
 INSERT dbo.PriceChannels(PriceChannelId,TenantId,Code,Name,Strategy,Value,IsActive,CreatedAt)
 VALUES(@Id,@TenantId,@Code,@Name,@Strategy,@Value,1,SYSUTCDATETIME());

 DECLARE @Items TABLE(ProductId UNIQUEIDENTIFIER NOT NULL,MinimumQuantity DECIMAL(19,6) NOT NULL,Amount DECIMAL(19,4) NOT NULL);
 INSERT @Items SELECT ProductId,MinimumQuantity,Amount FROM OPENJSON(@ItemsJson)
   WITH(ProductId UNIQUEIDENTIFIER '$.ProductId',MinimumQuantity DECIMAL(19,6) '$.MinimumQuantity',Amount DECIMAL(19,4) '$.Amount');
 IF EXISTS(SELECT 1 FROM @Items WHERE MinimumQuantity<=0 OR Amount<=0)
    OR EXISTS(SELECT ProductId,MinimumQuantity FROM @Items GROUP BY ProductId,MinimumQuantity HAVING COUNT(*)>1)
    OR EXISTS(SELECT 1 FROM @Items itemValue LEFT JOIN dbo.Products product
              ON product.ProductId=itemValue.ProductId AND product.TenantId=@TenantId AND product.IsActive=1
              WHERE product.ProductId IS NULL)
    THROW 51005,'Invalid product price tier.',1;
 INSERT dbo.PriceChannelItems(PriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
 SELECT NEWID(),@Id,ProductId,MinimumQuantity,Amount,N'COP',SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET() FROM @Items;

 DECLARE @Exclusions TABLE(ScopeType NVARCHAR(16) NOT NULL,ScopeId UNIQUEIDENTIFIER NOT NULL);
 INSERT @Exclusions SELECT ScopeType,ScopeId FROM OPENJSON(@ExclusionsJson)
   WITH(ScopeType NVARCHAR(16) '$.ScopeType',ScopeId UNIQUEIDENTIFIER '$.ScopeId');
 IF EXISTS(SELECT 1 FROM @Exclusions WHERE ScopeType NOT IN(N'Product',N'Category',N'Brand'))
    OR EXISTS(SELECT ScopeType,ScopeId FROM @Exclusions GROUP BY ScopeType,ScopeId HAVING COUNT(*)>1)
    OR EXISTS(SELECT 1 FROM @Exclusions exclusionValue
       WHERE (ScopeType=N'Product' AND NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=exclusionValue.ScopeId AND TenantId=@TenantId AND IsActive=1))
          OR (ScopeType=N'Category' AND NOT EXISTS(SELECT 1 FROM dbo.ProductCategories WHERE ProductCategoryId=exclusionValue.ScopeId AND TenantId=@TenantId AND IsActive=1))
          OR (ScopeType=N'Brand' AND NOT EXISTS(SELECT 1 FROM dbo.ProductBrands WHERE ProductBrandId=exclusionValue.ScopeId AND TenantId=@TenantId AND IsActive=1)))
    THROW 51005,'Invalid price channel exclusion.',1;
 INSERT dbo.PriceChannelExclusions(PriceChannelExclusionId,PriceChannelId,ScopeType,ProductId,ProductCategoryId,ProductBrandId,CreatedAt)
 SELECT NEWID(),@Id,ScopeType,
        CASE WHEN ScopeType=N'Product' THEN ScopeId END,
        CASE WHEN ScopeType=N'Category' THEN ScopeId END,
        CASE WHEN ScopeType=N'Brand' THEN ScopeId END,SYSDATETIMEOFFSET()
 FROM @Exclusions;
END
