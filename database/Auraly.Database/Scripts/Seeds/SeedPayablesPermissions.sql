SET NOCOUNT ON;
DECLARE @PayablesPermissions TABLE([Action] NVARCHAR(50) NOT NULL,[Resource] NVARCHAR(100) NOT NULL,[Description] NVARCHAR(500) NOT NULL);
INSERT @PayablesPermissions VALUES
    (N'Read',N'payables.read',N'Consultar obligaciones y movimientos de proveedores'),
    (N'Create',N'payables.payments.create',N'Registrar pagos aplicados a obligaciones de proveedores');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Payables',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @PayablesPermissions p
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource IN (N'payables.read',N'payables.payments.create')
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
