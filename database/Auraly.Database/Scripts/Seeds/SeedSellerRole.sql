SET NOCOUNT ON;

DECLARE @SellerPermissions TABLE ([Resource] NVARCHAR(100) NOT NULL PRIMARY KEY);
INSERT @SellerPermissions ([Resource]) VALUES
  (N'orders.read'),
  (N'orders.create'),
  (N'orders.update'),
  (N'routes.read'),
  (N'routes.visits.record'),
  (N'customers.read'),
  (N'parties.read'),
  (N'inventory.read');

INSERT dbo.AppRoles
  (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
SELECT NEWID(),tenant.TenantId,N'Vendedor',N'SELLER',
       N'Toma de pedidos y ejecución de rutas comerciales.',1,0,SYSUTCDATETIME()
FROM dbo.Tenants tenant
WHERE NOT EXISTS (
  SELECT 1 FROM dbo.AppRoles role
  WHERE role.TenantId=tenant.TenantId AND role.NormalizedName=N'SELLER');

INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),role.RoleId,permission.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles role
JOIN dbo.Permissions permission
  ON permission.Resource IN (SELECT Resource FROM @SellerPermissions)
WHERE role.NormalizedName=N'SELLER' AND role.IsActive=1
  AND NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions assigned
    WHERE assigned.RoleId=role.RoleId AND assigned.PermissionId=permission.PermissionId);
GO
