SET NOCOUNT ON;
DECLARE @RoutePermissions TABLE([Module] NVARCHAR(50),[Action] NVARCHAR(50),[Resource] NVARCHAR(100),[Description] NVARCHAR(500));
INSERT @RoutePermissions VALUES
(N'Routes',N'Read',N'routes.read',N'Consultar rutas comerciales y sus recorridos'),
(N'Routes',N'ReadAll',N'routes.read-all',N'Consultar las rutas asignadas a cualquier vendedor'),
(N'Routes',N'Create',N'routes.create',N'Crear rutas comerciales'),
(N'Routes',N'Update',N'routes.update',N'Editar datos y calendario de rutas comerciales'),
(N'Routes',N'Activate',N'routes.activate',N'Activar rutas comerciales'),
(N'Routes',N'Deactivate',N'routes.deactivate',N'Desactivar rutas comerciales'),
(N'Routes',N'ManageStops',N'routes.stops.manage',N'Asignar y ordenar establecimientos en rutas'),
(N'Routes',N'RecordVisits',N'routes.visits.record',N'Registrar clientes visitados u omitidos en la ruta diaria'),
(N'Routes',N'Export',N'routes.export',N'Imprimir o exportar recorridos'),
(N'RouteZones',N'Read',N'route-zones.read',N'Consultar zonas comerciales'),
(N'RouteZones',N'Manage',N'route-zones.manage',N'Crear y administrar zonas comerciales');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),permission.Module,permission.Action,permission.Resource,permission.Description,SYSUTCDATETIME()
FROM @RoutePermissions permission
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions existing WHERE existing.Resource=permission.Resource);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),role.RoleId,permission.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles role
INNER JOIN dbo.Permissions permission ON permission.Resource IN (SELECT Resource FROM @RoutePermissions)
WHERE role.IsActive=1 AND role.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions assignment WHERE assignment.RoleId=role.RoleId AND assignment.PermissionId=permission.PermissionId);
GO
