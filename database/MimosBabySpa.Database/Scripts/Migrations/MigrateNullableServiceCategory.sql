-- =============================================================================
-- MigrateNullableServiceCategory.sql
--
-- Permite servicios sin categoria. Las categorias genericas "Otros" se eliminan
-- y sus servicios quedan con CategoryId NULL.
-- =============================================================================

SET NOCOUNT ON;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Services')
      AND name = N'CategoryId'
      AND is_nullable = 0
)
BEGIN
    ALTER TABLE dbo.Services DROP CONSTRAINT FK_Services_ServiceCategories;
    ALTER TABLE dbo.Services ALTER COLUMN CategoryId UNIQUEIDENTIFIER NULL;
    ALTER TABLE dbo.Services WITH CHECK ADD CONSTRAINT FK_Services_ServiceCategories
        FOREIGN KEY (CategoryId) REFERENCES dbo.ServiceCategories(ServiceCategoryId);
END

UPDATE s
SET s.CategoryId = NULL,
    s.UpdatedAt = GETUTCDATE()
FROM dbo.Services s
INNER JOIN dbo.ServiceCategories c ON c.ServiceCategoryId = s.CategoryId
WHERE c.Name = N'Otros';

DELETE c
FROM dbo.ServiceCategories c
WHERE c.Name = N'Otros'
  AND NOT EXISTS (SELECT 1 FROM dbo.Services s WHERE s.CategoryId = c.ServiceCategoryId);

UPDATE s
SET s.CategoryId = NULL,
    s.UpdatedAt = GETUTCDATE()
FROM dbo.Services s
WHERE s.CategoryId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories c WHERE c.ServiceCategoryId = s.CategoryId);

PRINT N'MigrateNullableServiceCategory: Services.CategoryId permite NULL y Otros fue removida.';
GO
