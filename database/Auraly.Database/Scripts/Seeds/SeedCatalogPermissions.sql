SET NOCOUNT ON;

DECLARE @CatalogPermissions TABLE(
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL);

INSERT @CatalogPermissions VALUES
    (N'Read',N'catalog.read',N'Consultar productos y cat?logo comercial'),
    (N'Create',N'catalog.create',N'Crear productos en el cat?logo'),
    (N'Update',N'catalog.update',N'Actualizar productos del cat?logo'),
    (N'Deactivate',N'catalog.deactivate',N'Activar o desactivar productos'),
    (N'ManagePrices',N'catalog.prices.manage',N'Administrar precios del cat?logo'),
    (N'ReadCosts',N'catalog.costs.read',N'Consultar costos de productos'),
    (N'ManageCosts',N'catalog.costs.manage',N'Administrar costos de productos');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Catalog',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @CatalogPermissions p
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.Permissions existing WHERE existing.Resource=p.Resource);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),role.RoleId,permission.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles role
JOIN dbo.Permissions permission
  ON permission.Resource IN (
      N'catalog.read',N'catalog.create',N'catalog.update',N'catalog.deactivate',
      N'catalog.prices.manage',N'catalog.costs.read',N'catalog.costs.manage')
WHERE role.IsActive=1
  AND role.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS(
      SELECT 1
      FROM dbo.RolePermissions assigned
      WHERE assigned.RoleId=role.RoleId
        AND assigned.PermissionId=permission.PermissionId);

