SET NOCOUNT ON;
DECLARE @FiscalPermissions TABLE([Action] NVARCHAR(50),[Resource] NVARCHAR(100),[Description] NVARCHAR(500));
INSERT @FiscalPermissions VALUES
 (N'Read',N'fiscal.configuration.read',N'Consultar la configuración fiscal de una sede'),
 (N'Manage',N'fiscal.configuration.manage',N'Configurar resoluciones y series fiscales');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Fiscal',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @FiscalPermissions p WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r CROSS JOIN dbo.Permissions p
WHERE r.IsActive=1 AND r.NormalizedName IN(N'ADMINISTRATOR',N'SUPERADMIN')
 AND p.Resource IN(N'fiscal.configuration.read',N'fiscal.configuration.manage')
 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
