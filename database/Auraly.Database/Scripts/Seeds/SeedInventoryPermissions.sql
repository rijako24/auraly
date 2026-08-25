SET NOCOUNT ON;
DECLARE @InventoryPermissions TABLE([Action] NVARCHAR(50) NOT NULL,[Resource] NVARCHAR(100) NOT NULL,[Description] NVARCHAR(500) NOT NULL);
INSERT @InventoryPermissions VALUES
(N'Read',N'inventory.read',N'Consultar existencias, kardex y operaciones de inventario'),
(N'ReadCosts',N'inventory.costs.read',N'Consultar costos y valorización del inventario'),
(N'Manage',N'inventory.warehouses.manage',N'Crear, editar, activar y desactivar bodegas'),
(N'Manage',N'inventory.reasons.manage',N'Crear, editar, activar y desactivar motivos de inventario'),
(N'Manage',N'inventory.physical-counts.manage',N'Crear, coordinar y cerrar inventarios físicos'),
(N'Capture',N'inventory.physical-counts.capture',N'Capturar preconteos y conteos de inventarios físicos'),
(N'Confirm',N'inventory.counts.confirm',N'Confirmar conteos físicos'),
(N'Confirm',N'inventory.adjustments.confirm',N'Confirmar ajustes de inventario'),
(N'Confirm',N'inventory.transfers.confirm',N'Confirmar traslados entre bodegas'),
(N'Confirm',N'inventory.conversions.confirm',N'Confirmar conversiones de productos'),
(N'Confirm',N'inventory.damages.confirm',N'Registrar averías de inventario');
INSERT dbo.Permissions(PermissionId,[Module],[Action],[Resource],[Description],CreatedAt)
SELECT NEWID(),N'Inventory',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @InventoryPermissions p WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r JOIN dbo.Permissions p ON p.Resource IN
(N'inventory.read',N'inventory.costs.read',N'inventory.warehouses.manage',N'inventory.reasons.manage',N'inventory.physical-counts.manage',N'inventory.physical-counts.capture',N'inventory.counts.confirm',N'inventory.adjustments.confirm',N'inventory.transfers.confirm',N'inventory.conversions.confirm',N'inventory.damages.confirm')
WHERE r.IsActive=1 AND (r.NormalizedName=N'ADMINISTRATOR' OR EXISTS (SELECT 1 FROM dbo.RolePermissions existingRp JOIN dbo.Permissions existingPermission ON existingPermission.PermissionId=existingRp.PermissionId WHERE existingRp.RoleId=r.RoleId AND existingPermission.Resource=N'inventory.read'))
AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
