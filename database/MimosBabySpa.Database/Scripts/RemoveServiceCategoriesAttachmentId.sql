-- ============================================================
-- Script: RemoveServiceCategoriesAttachmentId
-- Elimina la columna AttachmentId y su FK de ServiceCategories.
-- Ejecutar en BD que ya tiene ServiceCategories con AttachmentId.
-- ============================================================

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ServiceCategories') AND name = 'AttachmentId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServiceCategories_AttachmentId' AND object_id = OBJECT_ID('dbo.ServiceCategories'))
        DROP INDEX [IX_ServiceCategories_AttachmentId] ON [dbo].[ServiceCategories];
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ServiceCategories_BusinessAttachments')
        ALTER TABLE [dbo].[ServiceCategories] DROP CONSTRAINT [FK_ServiceCategories_BusinessAttachments];
    ALTER TABLE [dbo].[ServiceCategories] DROP COLUMN [AttachmentId];
    PRINT N'AttachmentId eliminado de ServiceCategories.';
END
ELSE
    PRINT N'ServiceCategories ya no tiene AttachmentId.';
GO
