SET NOCOUNT ON;
DECLARE @TaxationPermissions TABLE([Action] NVARCHAR(50) NOT NULL,[Resource] NVARCHAR(100) NOT NULL,[Description] NVARCHAR(500) NOT NULL);
INSERT @TaxationPermissions VALUES
    (N'Read',N'commerce.taxation.withholdings.view',N'Consultar y previsualizar reglas de retencion'),
    (N'Configure',N'commerce.taxation.withholdings.manage',N'Crear nuevas versiones de reglas de retencion');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Taxation',p.Action,p.Resource,p.Description,SYSUTCDATETIME() FROM @TaxationPermissions p
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r JOIN dbo.Permissions p ON p.Resource IN
  (N'commerce.taxation.withholdings.view',N'commerce.taxation.withholdings.manage')
WHERE r.IsActive=1 AND r.NormalizedName IN (N'ADMINISTRATOR',N'ADMINISTRATIVE',N'ACCOUNTANT')
AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
