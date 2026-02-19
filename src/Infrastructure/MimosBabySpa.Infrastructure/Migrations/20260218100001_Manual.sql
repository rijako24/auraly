-- ============================================================
-- Migración: 20260218100001_RemoveCumplemesPlanMarineritosSencilla
-- Elimina el servicio "Cumplemes – Plan Marineritos + Sencilla"
-- Reservas existentes se migran a Plan Marineritos
-- ============================================================

PRINT '=== Eliminando Cumplemes – Plan Marineritos + Sencilla ===';
GO

DECLARE @ServiceToRemove UNIQUEIDENTIFIER = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';
DECLARE @FallbackService UNIQUEIDENTIFIER = 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA';

-- 1. Reservas: migrar a Plan Marineritos
UPDATE [dbo].[Reservations]
SET [ServiceId] = @FallbackService, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = @ServiceToRemove;

PRINT 'Reservas actualizadas.';
GO

-- 2. EmployeeServices
DELETE FROM [dbo].[EmployeeServices]
WHERE [ServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';

-- 3. ServiceBundleItems
DELETE FROM [dbo].[ServiceBundleItems]
WHERE [BundleServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';

-- 4. ServiceResourceUsages
DELETE FROM [dbo].[ServiceResourceUsages]
WHERE [ServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';

-- 5. Servicio
DELETE FROM [dbo].[Services]
WHERE [ServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';

PRINT 'Servicio eliminado.';
GO

-- Registro de migración
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260218100001_RemoveCumplemesPlanMarineritosSencilla')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218100001_RemoveCumplemesPlanMarineritosSencilla', N'8.0.0');
END
GO

PRINT '✅ Cumplemes – Plan Marineritos + Sencilla eliminado.';
GO
