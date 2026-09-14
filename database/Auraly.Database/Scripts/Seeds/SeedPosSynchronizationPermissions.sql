DECLARE @PosPermissions TABLE(
    Module NVARCHAR(50) NOT NULL,
    Action NVARCHAR(50) NOT NULL,
    Resource NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NOT NULL);
INSERT @PosPermissions(Module,Action,Resource,Description)
VALUES
    (N'Sales',N'Create',N'sales.create',N'Crear y emitir ventas desde una caja'),
    (N'Sales',N'ChangePrice',N'sales.change-price',N'Cambiar costo permitido, margen, descuento o precio de una línea de venta'),
    (N'Sales',N'ReadCostAndMargin',N'sales.lines.cost-margin.read',N'Ver costo y margen en la edición de líneas de venta'),
    (N'Sales',N'ChangeDescription',N'sales.lines.change-description',N'Editar la descripción de una línea de venta'),
    (N'Sales',N'ProratedDiscount',N'sales.lines.prorated-discount',N'Aplicar un descuento general prorrateado entre las líneas de una venta'),
    (N'Sales',N'SellBelowCost',N'sales.below-cost',N'Confirmar ventas cuyo neto queda por debajo del costo'),
    (N'Sales',N'Reprint',N'sales.reprint',N'Reimprimir facturas con trazabilidad'),
    (N'Sales',N'Void',N'sales.void',N'Eliminar líneas o reiniciar ventas');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),p.Module,p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @PosPermissions p
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.Permissions existing WHERE existing.Resource=p.Resource);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p
  ON p.Resource IN(N'sales.create',N'sales.change-price',N'sales.lines.cost-margin.read',N'sales.lines.change-description',N'sales.lines.prorated-discount',N'sales.reprint',N'sales.void')
WHERE r.IsActive=1
  AND r.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS(
      SELECT 1 FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource=N'sales.below-cost'
WHERE r.IsActive=1 AND r.NormalizedName=N'SUPERVISOR'
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp
                 WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);

DELETE assignment
FROM dbo.RolePermissions assignment
INNER JOIN dbo.Permissions permissionValue ON permissionValue.PermissionId=assignment.PermissionId
WHERE permissionValue.Resource IN(
    N'pos.synchronization.events.read',N'pos.approvals.receive_notifications',N'pos.orders');

DELETE FROM dbo.Permissions
WHERE Resource IN(
    N'pos.synchronization.events.read',N'pos.approvals.receive_notifications',N'pos.orders');
GO
