SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Insert-only: preserve labels, availability and role customizations on reruns.
INSERT reference.Options
    (OptionId,CatalogCode,Code,Label,Description,IsActive,SortOrder,CreatedAt,UpdatedAt)
SELECT source.Id,N'point-of-sale-mode',source.Code,source.Label,source.Description,
       1,source.SortOrder,SYSUTCDATETIME(),SYSUTCDATETIME()
FROM (VALUES
    (CAST('1a900000-0000-0000-0000-000000000001' AS uniqueidentifier),N'DirectSale',N'Venta directa',N'Punto de venta comercial.',10),
    (CAST('1a900000-0000-0000-0000-000000000002' AS uniqueidentifier),N'RestaurantBar',N'Restaurante y bar',N'Atención por mesas y comandas.',20)
) source(Id,Code,Label,Description,SortOrder)
WHERE NOT EXISTS (
    SELECT 1 FROM reference.Options existing
    WHERE existing.CatalogCode=N'point-of-sale-mode' AND existing.Code=source.Code);

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Restaurant',N'Attend',N'restaurant.attend',
       N'Atender mesas, tomar consumos, enviar comandas y generar precuentas sin facturar ni cobrar.',
       SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Resource=N'restaurant.attend');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Restaurant',N'OpenWithoutWaiter',
       N'restaurant.tables.open_without_waiter',
       N'Abrir mesas sin seleccionar mesero; los productos agregados quedan a nombre del usuario conectado.',
       SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions
    WHERE Resource=N'restaurant.tables.open_without_waiter');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Restaurant',N'ReadCommands',N'restaurant.commands.read',
       N'Consultar la pantalla de comandas de las estaciones.',SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Resource=N'restaurant.commands.read');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Restaurant',N'ManageCommands',N'restaurant.commands.manage',
       N'Actualizar el estado de las comandas en cocina y barra.',SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Resource=N'restaurant.commands.manage');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Restaurant',N'ApproveCancellations',N'restaurant.cancellations.approve',
       N'Aprobar o rechazar cancelaciones de productos enviados a comanda.',SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Resource=N'restaurant.cancellations.approve');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Parties',source.Action,source.Resource,source.Description,SYSUTCDATETIME()
FROM (VALUES
    (N'CreateWaiter',N'parties.waiters.create',N'Agregar el rol comercial Mesero sin crear usuarios ni asignar acceso.'),
    (N'ConfigureWaiter',N'parties.waiters.configure',N'Configurar la ficha de Mesero compartida por todas las sedes del tenant.')
) source(Action,Resource,Description)
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions existing WHERE existing.Resource=source.Resource);

INSERT reference.Options(OptionId,CatalogCode,Code,Label,Description,IsActive,SortOrder,CreatedAt,UpdatedAt)
SELECT source.Id,N'party-role',source.Code,source.Label,source.Description,1,
    source.SortOrder,SYSUTCDATETIME(),SYSUTCDATETIME()
FROM (VALUES
    (CAST('1a910000-0000-0000-0000-000000000001' AS uniqueidentifier),N'Customer',N'Cliente',N'Ventas, precios y cartera',10),
    (CAST('1a910000-0000-0000-0000-000000000002' AS uniqueidentifier),N'Supplier',N'Proveedor',N'Compras y cuentas por pagar',20),
    (CAST('1a910000-0000-0000-0000-000000000003' AS uniqueidentifier),N'Seller',N'Vendedor',N'Comisiones, rutas y atención comercial',30),
    (CAST('1a910000-0000-0000-0000-000000000004' AS uniqueidentifier),N'Carrier',N'Transportador',N'Despachos y transporte de mercancía',40),
    (CAST('1a910000-0000-0000-0000-000000000005' AS uniqueidentifier),N'Employee',N'Empleado',N'Servicios, disponibilidad y calendario',50),
    (CAST('1a910000-0000-0000-0000-000000000006' AS uniqueidentifier),N'User',N'Usuario',N'Acceso, contraseña y permisos del sistema',60),
    (CAST('1a910000-0000-0000-0000-000000000007' AS uniqueidentifier),N'Waiter',N'Mesero',N'Mesas, consumos y comandas. El usuario se vincula manualmente.',70)
) source(Id,Code,Label,Description,SortOrder)
WHERE NOT EXISTS(SELECT 1 FROM reference.Options existing
    WHERE existing.CatalogCode=N'party-role' AND existing.Code=source.Code);

EXEC dbo.RestaurantWaiterRoleEnsure;

-- Existing restaurant tenants receive the same default access as newly
-- provisioned restaurant tenants. Direct-sale tenants are left untouched.
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),role.RoleId,permission.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles role
JOIN dbo.Permissions permission ON permission.Resource IN
    (N'restaurant.attend',N'restaurant.commands.read',N'restaurant.commands.manage',
     N'restaurant.cancellations.approve',N'restaurant.tables.open_without_waiter')
WHERE (role.NormalizedName IN (N'ADMINISTRATOR',N'ADMINISTRATIVE')
       OR role.NormalizedName IN (N'CASHIER',N'SUPERVISOR')
          AND permission.Resource=N'restaurant.tables.open_without_waiter')
  AND EXISTS (SELECT 1 FROM dbo.Businesses business
      WHERE business.TenantId=role.TenantId AND business.IsActive=1
        AND business.PointOfSaleMode=N'RestaurantBar')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions existing
      WHERE existing.RoleId=role.RoleId AND existing.PermissionId=permission.PermissionId);

-- The platform administrator is provisioned earlier in PostDeployment. Keep its
-- global permission set complete when a new restaurant permission is introduced.
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),role.RoleId,permission.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles role
JOIN dbo.Tenants tenant ON tenant.TenantId=role.TenantId AND tenant.TenantKey=N'@auraly'
JOIN dbo.Permissions permission ON permission.Resource=N'restaurant.tables.open_without_waiter'
WHERE role.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions existing
      WHERE existing.RoleId=role.RoleId AND existing.PermissionId=permission.PermissionId);
COMMIT TRANSACTION;
GO
