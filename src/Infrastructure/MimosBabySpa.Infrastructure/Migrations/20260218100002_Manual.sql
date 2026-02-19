-- ============================================================
-- Migración: 20260218100002_CleanupServicesAndDropCompatibleServiceCategory
-- - Elimina servicios 0005, 0010, 0011
-- - Categoriza y asigna ServiceType a todos
-- - Elimina CompatibleServiceCategory de ServiceAddOnRules
-- ============================================================

PRINT '=== Iniciando 20260218100002 ===';
GO

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @PlanMarineritos UNIQUEIDENTIFIER = 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA';

-- 1. Eliminar servicios 0005, 0010, 0011
UPDATE [dbo].[Reservations] SET [ServiceId] = @PlanMarineritos, [UpdatedAt] = GETUTCDATE() WHERE [ServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';
DELETE FROM [dbo].[EmployeeServices] WHERE [ServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';
DELETE FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';
DELETE FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';
DELETE FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';

UPDATE [dbo].[Reservations] SET [ServiceId] = @PlanMarineritos, [UpdatedAt] = GETUTCDATE() WHERE [ServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA';
DELETE FROM [dbo].[EmployeeServices] WHERE [ServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA';
DELETE FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA';
DELETE FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA';
DELETE FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA';

UPDATE [dbo].[Reservations] SET [ServiceId] = @PlanMarineritos, [UpdatedAt] = GETUTCDATE() WHERE [ServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA';
DELETE FROM [dbo].[EmployeeServices] WHERE [ServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA';
DELETE FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA';
DELETE FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA';
DELETE FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA';

PRINT 'Servicios eliminados.';
GO

-- 2. Categorizar y ServiceType (Plan=0, Taller=1, Clase=2, Otro=99 | Standard=0, AddOn=1)
UPDATE [dbo].[Services] SET [Category] = 0, [ServiceType] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222' AND [ServiceName] IN (N'Plan Marineritos', N'Plan Aventuras Marinas', N'Plan Suaves Mimos – Post Vacunas');

UPDATE [dbo].[Services] SET [Category] = 1, [ServiceType] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222' AND ([ServiceName] LIKE N'Taller Grupal%' OR [ServiceName] = N'Taller Grupal de Estimulación Temprana');

UPDATE [dbo].[Services] SET [Category] = 2, [ServiceType] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222' AND [ServiceName] = N'Clase Personalizada de Estimulación Temprana';

UPDATE [dbo].[Services] SET [Category] = 0, [ServiceType] = 1, [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222' AND [ServiceName] IN (N'Decoración Sencilla', N'Decoración Bouquet Personalizado');

PRINT 'Categorías y ServiceType asignados.';
GO

-- 3. Eliminar CompatibleServiceCategory de ServiceAddOnRules
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND name = 'CompatibleServiceCategory')
BEGIN
    ALTER TABLE [dbo].[ServiceAddOnRules] DROP COLUMN [CompatibleServiceCategory];
    PRINT 'Columna CompatibleServiceCategory eliminada.';
END
GO

-- 4. Registro
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260218100002_CleanupServicesAndDropCompatibleServiceCategory')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218100002_CleanupServicesAndDropCompatibleServiceCategory', N'8.0.0');
END
GO

PRINT '✅ Migración 20260218100002 aplicada.';
GO
