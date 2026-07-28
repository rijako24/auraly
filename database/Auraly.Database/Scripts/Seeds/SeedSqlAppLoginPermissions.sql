-- ============================================================

-- Script: SeedSqlAppLoginPermissions

-- Permisos del login SQL usado por la app (admin / masterkey en local).

-- Idempotente: solo agrega roles si el usuario existe y aún no los tiene.

-- ============================================================



SET NOCOUNT ON;



IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'admin' AND type = 'S')

BEGIN

    PRINT N'SeedSqlAppLoginPermissions: usuario SQL [admin] no existe — omitido.';

    RETURN;

END



IF NOT EXISTS (

    SELECT 1

    FROM sys.database_role_members drm

    JOIN sys.database_principals dp ON drm.member_principal_id = dp.principal_id

    JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id

    WHERE dp.name = N'admin' AND r.name = N'db_datareader')

BEGIN

    ALTER ROLE db_datareader ADD MEMBER [admin];

    PRINT N'SeedSqlAppLoginPermissions: db_datareader concedido a [admin].';

END



IF NOT EXISTS (

    SELECT 1

    FROM sys.database_role_members drm

    JOIN sys.database_principals dp ON drm.member_principal_id = dp.principal_id

    JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id

    WHERE dp.name = N'admin' AND r.name = N'db_datawriter')

BEGIN

    ALTER ROLE db_datawriter ADD MEMBER [admin];

    PRINT N'SeedSqlAppLoginPermissions: db_datawriter concedido a [admin].';

END



PRINT N'SeedSqlAppLoginPermissions: permisos de [admin] verificados.';

GO

