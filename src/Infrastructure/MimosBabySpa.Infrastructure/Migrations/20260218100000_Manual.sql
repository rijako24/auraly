-- ============================================================
-- Migración: 20260218100000_ReplaceGroupKeyWithServiceCategory
-- Ejecutar en SSMS o sqlcmd contra la base talkioai
-- ORDEN: Después de 20260218000007 (AddServiceBundleItems)
-- Sustituye GroupKey por ServiceCategory.
-- Category: Plan=0, Taller=1, Clase=2, Otro=99
-- ============================================================

PRINT '=== Iniciando migración 20260218100000 ===';
GO

-- ── 1. Añadir columna Category a Services ────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'Category')
BEGIN
    ALTER TABLE [dbo].[Services] ADD [Category] INT NOT NULL DEFAULT 99;
    PRINT 'Columna Category agregada.';
END
ELSE
    PRINT 'Columna Category ya existe.';
GO

-- ── 2. Añadir columna ServiceType a Services ─────────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'ServiceType')
BEGIN
    ALTER TABLE [dbo].[Services] ADD [ServiceType] INT NOT NULL DEFAULT 0;
    PRINT 'Columna ServiceType agregada.';
END
GO

-- ── 3. Crear tabla ServiceAddOnRules si no existe ────────────

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ServiceAddOnRules]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[ServiceAddOnRules] (
        [ServiceAddOnRuleId]       UNIQUEIDENTIFIER NOT NULL,
        [BusinessId]               UNIQUEIDENTIFIER NOT NULL,
        [AddOnServiceId]           UNIQUEIDENTIFIER NOT NULL,
        [CompatibleServiceId]      UNIQUEIDENTIFIER NULL,
        [CompatibleServiceCategory] INT NULL,
        [DisplayOrder]             INT NOT NULL DEFAULT 1,
        CONSTRAINT [PK_ServiceAddOnRules] PRIMARY KEY ([ServiceAddOnRuleId]),
        CONSTRAINT [FK_ServiceAddOnRules_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ServiceAddOnRules_AddOnService] FOREIGN KEY ([AddOnServiceId]) REFERENCES [dbo].[Services]([ServiceId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ServiceAddOnRules_CompatibleService] FOREIGN KEY ([CompatibleServiceId]) REFERENCES [dbo].[Services]([ServiceId]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_ServiceAddOnRules_BusinessId] ON [dbo].[ServiceAddOnRules]([BusinessId]);
    CREATE UNIQUE INDEX [IX_ServiceAddOnRules_BusinessId_AddOnServiceId_CompatibleServiceId] ON [dbo].[ServiceAddOnRules]([BusinessId], [AddOnServiceId], [CompatibleServiceId]);
    PRINT 'Tabla ServiceAddOnRules creada.';
END
GO

-- ── 4. Añadir CompatibleServiceCategory si falta ─────────────

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND type = 'U')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND name = 'CompatibleServiceCategory')
BEGIN
    ALTER TABLE [dbo].[ServiceAddOnRules] ADD [CompatibleServiceCategory] INT NULL;
    PRINT 'Columna CompatibleServiceCategory agregada.';
END
GO

-- ── 5. Migrar datos de GroupKey a Category (Mimos) ───────────

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

-- Plan Marineritos: Deluxe
UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 2, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Plan Marineritos';

-- Plan Aventuras: Base
UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Plan Aventuras Marinas';

-- Plan Suaves Mimos: Base
UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Plan Suaves Mimos – Post Vacunas';

-- Cumplemes: Plan, Base
UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [ServiceName] LIKE N'Cumplemes%';

-- Taller Grupal: Taller + Tiers
UPDATE [dbo].[Services] SET [Category] = 1, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND ([ServiceName] LIKE N'Taller Grupal%' OR [ServiceName] = N'Taller Grupal de Estimulación Temprana');

UPDATE [dbo].[Services] SET [Tier] = 2 WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Taller Grupal - 3 días/semana';
UPDATE [dbo].[Services] SET [Tier] = 1 WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Taller Grupal - 2 días/semana';
UPDATE [dbo].[Services] SET [Tier] = 0 WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Taller Grupal - 1 día/semana';
UPDATE [dbo].[Services] SET [Tier] = 0 WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Taller Grupal - Clase Individual';

-- Clase Personalizada: Clase
UPDATE [dbo].[Services] SET [Category] = 2, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [ServiceName] = N'Clase Personalizada de Estimulación Temprana';

PRINT 'Datos migrados.';
GO

-- ── 6. ServiceType=AddOn para Decoración Sencilla y Bouquet ───

UPDATE [dbo].[Services] SET [ServiceType] = 1, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] IN ('AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA', 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA')
  AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'ServiceType');
GO

-- ── 7. Seed ServiceAddOnRules (Decoración compatible con Plan) ─

DECLARE @BizId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceAddOnRules] WHERE [BusinessId] = @BizId AND [AddOnServiceId] = 'AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA' AND [CompatibleServiceId] IS NULL)
    INSERT INTO [dbo].[ServiceAddOnRules] ([ServiceAddOnRuleId], [BusinessId], [AddOnServiceId], [CompatibleServiceId], [CompatibleServiceCategory], [DisplayOrder])
    VALUES (NEWID(), @BizId, 'AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA', NULL, 0, 1);

IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceAddOnRules] WHERE [BusinessId] = @BizId AND [AddOnServiceId] = 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA' AND [CompatibleServiceId] IS NULL)
    INSERT INTO [dbo].[ServiceAddOnRules] ([ServiceAddOnRuleId], [BusinessId], [AddOnServiceId], [CompatibleServiceId], [CompatibleServiceCategory], [DisplayOrder])
    VALUES (NEWID(), @BizId, 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA', NULL, 0, 2);
GO

-- ── 8. Eliminar GroupKey y crear índice Category ──────────────

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Services_BusinessId_GroupKey' AND object_id = OBJECT_ID('dbo.Services'))
BEGIN
    DROP INDEX [IX_Services_BusinessId_GroupKey] ON [dbo].[Services];
    PRINT 'Índice IX_Services_BusinessId_GroupKey eliminado.';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'GroupKey')
BEGIN
    ALTER TABLE [dbo].[Services] DROP COLUMN [GroupKey];
    PRINT 'Columna GroupKey eliminada.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Services_BusinessId_Category' AND object_id = OBJECT_ID('dbo.Services'))
BEGIN
    CREATE INDEX [IX_Services_BusinessId_Category] ON [dbo].[Services] ([BusinessId], [Category]);
    PRINT 'Índice IX_Services_BusinessId_Category creado.';
END
GO

-- ── 9. Registro de migración ──────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260218100000_ReplaceGroupKeyWithServiceCategory')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218100000_ReplaceGroupKeyWithServiceCategory', N'8.0.0');
    PRINT 'Migración registrada.';
END
GO

PRINT '✅ Migración 20260218100000 aplicada correctamente.';
GO
