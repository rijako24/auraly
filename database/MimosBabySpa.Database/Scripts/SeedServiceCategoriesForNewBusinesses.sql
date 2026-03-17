-- ============================================================
-- Script: SeedServiceCategoriesForNewBusinesses
-- Inserta categorías (Plan, Taller, Clase, Otros) para negocios que aún no tengan.
-- Opcional: crea adjunto indicaciones para uso en mensajes de confirmación (Key=8).
-- ============================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER;
DECLARE @PlanCatId UNIQUEIDENTIFIER;

DECLARE biz_cursor CURSOR FOR
    SELECT b.BusinessId FROM dbo.Businesses b
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories sc WHERE sc.BusinessId = b.BusinessId);

OPEN biz_cursor;
FETCH NEXT FROM biz_cursor INTO @BusinessId;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessId = @BusinessId AND BlobPath = N'confirmations/indicaciones-para-tu-visita.pdf')
    BEGIN
        INSERT INTO dbo.BusinessAttachments (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
        VALUES (NEWID(), @BusinessId, N'confirmations/indicaciones-para-tu-visita.pdf', N'document', N'Indicaciones-para-tu-visita.pdf', N'Indicaciones para la visita', 1, GETUTCDATE());
    END

    SET @PlanCatId = NEWID();
    INSERT INTO dbo.ServiceCategories (ServiceCategoryId, BusinessId, Name, DisplayOrder, IsActive, CreatedAt)
    VALUES
        (@PlanCatId, @BusinessId, N'Plan', 0, 1, GETUTCDATE()),
        (NEWID(), @BusinessId, N'Taller', 1, 1, GETUTCDATE()),
        (NEWID(), @BusinessId, N'Clase', 2, 1, GETUTCDATE()),
        (NEWID(), @BusinessId, N'Otros', 99, 1, GETUTCDATE());

    FETCH NEXT FROM biz_cursor INTO @BusinessId;
END

CLOSE biz_cursor;
DEALLOCATE biz_cursor;

-- Corregir Services con CategoryId huérfano: asignar categoría "Plan" (primera) del mismo negocio
UPDATE s
SET s.CategoryId = (
    SELECT TOP 1 sc.ServiceCategoryId
    FROM dbo.ServiceCategories sc
    WHERE sc.BusinessId = s.BusinessId
    ORDER BY sc.DisplayOrder, sc.ServiceCategoryId
)
FROM dbo.Services s
WHERE NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories c WHERE c.ServiceCategoryId = s.CategoryId);

PRINT N'Seed de categorías completado.';
GO
