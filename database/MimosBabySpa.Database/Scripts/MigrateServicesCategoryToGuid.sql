-- ============================================================
-- Script: MigrateServicesCategoryToGuid
-- Para bases de datos EXISTENTES que tienen Services.Category (INT).
-- Ejecutar ANTES de desplegar el nuevo esquema con sqlproj.
--
-- Pasos: Crea BusinessAttachments, ServiceCategories, migra datos,
--        reemplaza Category por CategoryId.
-- ============================================================

SET NOCOUNT ON;

-- 1. Crear tablas si no existen
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BusinessAttachments')
BEGIN
    CREATE TABLE [dbo].[BusinessAttachments] (
        [BusinessAttachmentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [BusinessId] UNIQUEIDENTIFIER NOT NULL,
        [BlobPath] NVARCHAR(500) NOT NULL,
        [MediaType] NVARCHAR(50) NOT NULL DEFAULT 'document',
        [Filename] NVARCHAR(200) NULL,
        [Description] NVARCHAR(500) NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [FK_BusinessAttachments_Businesses] FOREIGN KEY ([BusinessId])
            REFERENCES [dbo].[Businesses]([BusinessId]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_BusinessAttachments_BusinessId] ON [dbo].[BusinessAttachments]([BusinessId]);
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ServiceCategories')
BEGIN
    CREATE TABLE [dbo].[ServiceCategories] (
        [ServiceCategoryId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [BusinessId] UNIQUEIDENTIFIER NOT NULL,
        [Name] NVARCHAR(100) NOT NULL,
        [DisplayOrder] INT NOT NULL DEFAULT 0,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_ServiceCategories_Businesses] FOREIGN KEY ([BusinessId])
            REFERENCES [dbo].[Businesses]([BusinessId]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_ServiceCategories_BusinessId_Name] ON [dbo].[ServiceCategories]([BusinessId],[Name]);
END

-- 2. Agregar CategoryId si existe Category (separar batch para que la columna exista antes del UPDATE)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'Category')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'CategoryId')
    ALTER TABLE dbo.Services ADD CategoryId UNIQUEIDENTIFIER NULL;
GO

-- 3. Migrar por cada negocio
DECLARE @BusinessId UNIQUEIDENTIFIER;
DECLARE @PlanId UNIQUEIDENTIFIER, @TallerId UNIQUEIDENTIFIER, @ClaseId UNIQUEIDENTIFIER, @OtrosId UNIQUEIDENTIFIER;

DECLARE bc CURSOR FOR SELECT BusinessId FROM dbo.Businesses;
OPEN bc;
FETCH NEXT FROM bc INTO @BusinessId;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories WHERE BusinessId = @BusinessId)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessId = @BusinessId AND BlobPath = N'confirmations/indicaciones-para-tu-visita.pdf')
            INSERT INTO dbo.BusinessAttachments (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)
            VALUES (NEWID(), @BusinessId, N'confirmations/indicaciones-para-tu-visita.pdf', N'document', N'Indicaciones-para-tu-visita.pdf', N'Indicaciones', 1, GETUTCDATE());

        SET @PlanId = NEWID();
    SET @TallerId = NEWID();
    SET @ClaseId = NEWID();
    SET @OtrosId = NEWID();

    INSERT INTO dbo.ServiceCategories (ServiceCategoryId, BusinessId, Name, DisplayOrder, IsActive, CreatedAt)
    VALUES (@PlanId, @BusinessId, N'Plan', 0, 1, GETUTCDATE()),
           (@TallerId, @BusinessId, N'Taller', 1, 1, GETUTCDATE()),
           (@ClaseId, @BusinessId, N'Clase', 2, 1, GETUTCDATE()),
           (@OtrosId, @BusinessId, N'Otros', 99, 1, GETUTCDATE());

        UPDATE s SET CategoryId = CASE s.Category WHEN 0 THEN @PlanId WHEN 1 THEN @TallerId WHEN 2 THEN @ClaseId ELSE @OtrosId END
        FROM dbo.Services s WHERE s.BusinessId = @BusinessId;
    END
    ELSE
    BEGIN
        SELECT @PlanId = ServiceCategoryId FROM dbo.ServiceCategories WHERE BusinessId = @BusinessId AND Name = N'Plan';
        SELECT @TallerId = ServiceCategoryId FROM dbo.ServiceCategories WHERE BusinessId = @BusinessId AND Name = N'Taller';
        SELECT @ClaseId = ServiceCategoryId FROM dbo.ServiceCategories WHERE BusinessId = @BusinessId AND Name = N'Clase';
        SELECT @OtrosId = ServiceCategoryId FROM dbo.ServiceCategories WHERE BusinessId = @BusinessId AND Name = N'Otros';
        UPDATE s SET CategoryId = CASE s.Category WHEN 0 THEN @PlanId WHEN 1 THEN @TallerId WHEN 2 THEN @ClaseId ELSE @OtrosId END
        FROM dbo.Services s WHERE s.BusinessId = @BusinessId AND s.CategoryId IS NULL;
    END

    FETCH NEXT FROM bc INTO @BusinessId;
END

CLOSE bc;
DEALLOCATE bc;

-- 4. Eliminar Category antigua y configurar CategoryId
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'Category')
BEGIN
    DROP INDEX IF EXISTS [IX_Services_BusinessId_Category] ON dbo.Services;
    ALTER TABLE dbo.Services DROP COLUMN Category;
    ALTER TABLE dbo.Services ALTER COLUMN CategoryId UNIQUEIDENTIFIER NOT NULL;
    ALTER TABLE dbo.Services ADD CONSTRAINT FK_Services_ServiceCategories FOREIGN KEY (CategoryId) REFERENCES dbo.ServiceCategories(ServiceCategoryId) ON DELETE NO ACTION;
    CREATE INDEX IX_Services_BusinessId_CategoryId ON dbo.Services(BusinessId, CategoryId);
END

PRINT N'Migración completada.';
GO
