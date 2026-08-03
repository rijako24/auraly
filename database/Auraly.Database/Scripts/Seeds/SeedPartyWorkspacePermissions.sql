SET NOCOUNT ON;
DECLARE @PartyPermissions TABLE([Module] NVARCHAR(50),[Action] NVARCHAR(50),[Resource] NVARCHAR(100),[Description] NVARCHAR(500));
INSERT @PartyPermissions VALUES
(N'Parties',N'Read',N'parties.read',N'Consultar el maestro unificado de terceros'),
(N'Parties',N'Update',N'parties.update',N'Editar identidad y contactos de terceros'),
(N'Parties',N'Deactivate',N'parties.deactivate',N'Activar o desactivar roles comerciales del tercero'),
(N'Customers',N'Read',N'customers.read',N'Consultar clientes'),
(N'Customers',N'Create',N'customers.create',N'Crear clientes'),
(N'Customers',N'ManageSites',N'parties.sites.manage',N'Administrar sedes de terceros'),
(N'Customers',N'ManagePricing',N'customers.pricing.manage',N'Administrar lista o canal del cliente'),
(N'Suppliers',N'Read',N'suppliers.read',N'Consultar proveedores'),
(N'Suppliers',N'Create',N'suppliers.create',N'Crear proveedores como rol de Party'),
(N'Masters',N'Read',N'masters.geography.read',N'Consultar maestros geográficos'),
(N'Masters',N'Manage',N'masters.geography.manage',N'Administrar maestros geográficos'),
(N'Security',N'LinkParty',N'security.users.link-party',N'Enlazar una cuenta de usuario con Party');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),p.Module,p.Action,p.Resource,p.Description,SYSUTCDATETIME() FROM @PartyPermissions p
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r JOIN dbo.Permissions p ON p.Resource IN (SELECT Resource FROM @PartyPermissions)
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
GO
