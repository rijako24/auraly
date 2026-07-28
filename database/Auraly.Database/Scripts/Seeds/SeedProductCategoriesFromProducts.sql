DECLARE @CategoryBackfillNow DATETIME2 = SYSUTCDATETIME();

;WITH SourceCategories AS
(
    SELECT
        product.BusinessId,
        product.IntegrationConnectionId,
        LTRIM(RTRIM(product.CategoryName)) AS [Name]
    FROM dbo.Products product
    WHERE NULLIF(LTRIM(RTRIM(product.CategoryName)), N'') IS NOT NULL
    GROUP BY
        product.BusinessId,
        product.IntegrationConnectionId,
        LTRIM(RTRIM(product.CategoryName))
)
MERGE dbo.ProductCategories AS target
USING SourceCategories AS source
ON target.BusinessId = source.BusinessId
   AND (target.IntegrationConnectionId = source.IntegrationConnectionId
        OR target.IntegrationConnectionId IS NULL AND source.IntegrationConnectionId IS NULL)
   AND target.[Name] = source.[Name]
WHEN NOT MATCHED BY TARGET THEN
    INSERT
    (
        ProductCategoryId,
        BusinessId,
        IntegrationConnectionId,
        ExternalCategoryId,
        [Name],
        DisplayOrder,
        IsActive,
        IsBrowsable,
        LastSyncedAt,
        CreatedAt
    )
    VALUES
    (
        NEWID(),
        source.BusinessId,
        source.IntegrationConnectionId,
        NULL,
        source.[Name],
        0,
        1,
        1,
        @CategoryBackfillNow,
        @CategoryBackfillNow
    );

UPDATE product
SET
    product.ProductCategoryId = category.ProductCategoryId,
    product.CategoryName = category.[Name],
    product.UpdatedAt = @CategoryBackfillNow
FROM dbo.Products product
INNER JOIN dbo.ProductCategories category
    ON category.BusinessId = product.BusinessId
   AND (category.IntegrationConnectionId = product.IntegrationConnectionId
        OR category.IntegrationConnectionId IS NULL AND product.IntegrationConnectionId IS NULL)
   AND category.[Name] = LTRIM(RTRIM(product.CategoryName))
WHERE product.ProductCategoryId IS NULL
  AND NULLIF(LTRIM(RTRIM(product.CategoryName)), N'') IS NOT NULL;
