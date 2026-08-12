SET NOCOUNT ON;

DECLARE @OrderPermissions TABLE
(
    [Module] NVARCHAR(50) NOT NULL,
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL
);

INSERT INTO @OrderPermissions ([Module],[Action],[Resource],[Description])
VALUES
    (N'Orders',N'Read',N'orders.read',N'Consultar pedidos del negocio'),
    (N'Orders',N'Recover',N'orders.recover',N'Recuperar un pedido en la venta activa'),
    (N'Orders',N'Invoice',N'orders.invoice',N'Facturar uno o varios pedidos'),
    (N'Orders',N'Cancel',N'orders.cancel',N'Cancelar pedidos'),
    (N'Orders',N'OverridePricing',N'orders.override-pricing',N'Modificar valores comerciales de pedidos');

INSERT dbo.Permissions
    (PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),p.Module,p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @OrderPermissions p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Permissions existing WHERE existing.Resource=p.Resource);

INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),role.RoleId,permission.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles role
JOIN dbo.Permissions permission
  ON permission.Resource IN
  (
      N'orders.read',
      N'orders.recover',
      N'orders.invoice',
      N'orders.cancel',
      N'orders.override-pricing'
  )
WHERE role.IsActive=1
  AND role.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.RolePermissions existing
      WHERE existing.RoleId=role.RoleId
        AND existing.PermissionId=permission.PermissionId
  );
GO
