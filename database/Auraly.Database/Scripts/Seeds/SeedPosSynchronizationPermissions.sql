DECLARE @PosSynchronizationEventsPermissionId UNIQUEIDENTIFIER =
    CAST('019C0031-7B93-7B4A-873B-C07C4AB9D99F' AS UNIQUEIDENTIFIER);
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Resource=N'pos.synchronization.events.read')
    INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description)
    VALUES(@PosSynchronizationEventsPermissionId,N'POS',N'ReadSynchronizationEvents',
           N'pos.synchronization.events.read',N'Consultar eventos técnicos de sincronización de la caja');
ELSE
    SELECT @PosSynchronizationEventsPermissionId=PermissionId
    FROM dbo.Permissions WHERE Resource=N'pos.synchronization.events.read';

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId)
SELECT NEWID(),r.RoleId,@PosSynchronizationEventsPermissionId
FROM dbo.AppRoles r
WHERE r.NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR',N'CASHIER')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId=r.RoleId AND rp.PermissionId=@PosSynchronizationEventsPermissionId);

DECLARE @PosPermissions TABLE(
    Module NVARCHAR(50) NOT NULL,
    Action NVARCHAR(50) NOT NULL,
    Resource NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NOT NULL);
INSERT @PosPermissions(Module,Action,Resource,Description)
VALUES
    (N'Sales',N'Create',N'sales.create',N'Crear y emitir ventas desde una caja'),
    (N'Sales',N'Discount',N'sales.discount',N'Aplicar descuentos en líneas de venta'),
    (N'Sales',N'ChangePrice',N'sales.change-price',N'Editar precio y descuento de las líneas de una venta'),
    (N'Sales',N'ChangeDescription',N'sales.lines.change-description',N'Editar la descripción de una línea de venta'),
    (N'Sales',N'ReadCostAndMargin',N'sales.lines.cost-margin.read',N'Ver costo y margen en una línea de venta'),
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
  ON p.Resource IN(N'sales.create',N'sales.discount',N'sales.change-price',N'sales.reprint',N'sales.void')
WHERE r.IsActive=1
  AND r.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS(
      SELECT 1 FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p
  ON p.Resource IN(N'sales.lines.change-description',N'sales.lines.cost-margin.read',N'sales.below-cost')
WHERE r.IsActive=1 AND r.NormalizedName=N'SUPERVISOR'
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp
                 WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
GO
