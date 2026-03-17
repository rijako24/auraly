-- ============================================================
-- Script: InsertTestBusinessAttachments
-- Crea la tabla BusinessAttachments si no existe e inserta adjuntos para el negocio 22222222-2222-2222-2222-222222222222.
-- Ejecutar: sqlcmd -S .\LOCAL -d talkioai -U admin -P masterkey -C -i "InsertTestBusinessAttachments.sql"
-- ============================================================

SET NOCOUNT ON;

-- Crear tabla BusinessAttachments si no existe
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BusinessAttachments')
BEGIN
    CREATE TABLE [dbo].[BusinessAttachments] (
        [BusinessAttachmentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [BusinessId]           UNIQUEIDENTIFIER NOT NULL,
        [BlobPath]            NVARCHAR(500)   NOT NULL,
        [MediaType]           NVARCHAR(50)    NOT NULL DEFAULT 'document',
        [Filename]            NVARCHAR(200)   NULL,
        [Description]         NVARCHAR(500)   NULL,
        [IsActive]            BIT             NOT NULL DEFAULT 1,
        [CreatedAt]           DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [FK_BusinessAttachments_Businesses] FOREIGN KEY ([BusinessId])
            REFERENCES [dbo].[Businesses] ([BusinessId]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_BusinessAttachments_BusinessId] ON [dbo].[BusinessAttachments] ([BusinessId]);
    PRINT N'Tabla BusinessAttachments creada.';
END

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

-- Solo insertar si el negocio existe
IF EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)
BEGIN
    -- Evitar duplicados: insertar solo si no existen ya
    IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessAttachments] WHERE [BusinessId] = @BusinessId AND [BlobPath] = N'Terminos y condiciones planes Baby Spa.pdf')
    BEGIN
        INSERT INTO [dbo].[BusinessAttachments] ([BusinessAttachmentId], [BusinessId], [BlobPath], [MediaType], [Filename], [Description], [IsActive], [CreatedAt])
        VALUES (NEWID(), @BusinessId, N'Terminos y condiciones planes Baby Spa.pdf', N'document', N'Terminos y condiciones planes Baby Spa.pdf', N'Términos y condiciones planes Baby Spa', 1, GETUTCDATE());
        PRINT N'Insertado: Terminos y condiciones planes Baby Spa.pdf';
    END

    IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessAttachments] WHERE [BusinessId] = @BusinessId AND [BlobPath] = N'Terminos y condiciones programa de estimulación temprana..pdf')
    BEGIN
        INSERT INTO [dbo].[BusinessAttachments] ([BusinessAttachmentId], [BusinessId], [BlobPath], [MediaType], [Filename], [Description], [IsActive], [CreatedAt])
        VALUES (NEWID(), @BusinessId, N'Terminos y condiciones programa de estimulación temprana..pdf', N'document', N'Terminos y condiciones programa de estimulación temprana.pdf', N'Términos y condiciones programa de estimulación temprana', 1, GETUTCDATE());
        PRINT N'Insertado: Terminos y condiciones programa de estimulación temprana..pdf';
    END
END
ELSE
BEGIN
    PRINT N'Negocio 22222222-2222-2222-2222-222222222222 no existe. Crear el negocio primero.';
END
GO
