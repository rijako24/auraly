SET NOCOUNT ON;
DECLARE @AccountingPermissions TABLE([Action] NVARCHAR(50) NOT NULL,[Resource] NVARCHAR(100) NOT NULL,[Description] NVARCHAR(500) NOT NULL);
INSERT @AccountingPermissions VALUES
    (N'Read',N'accounting.read',N'Consultar comprobantes y reportes contables'),
    (N'Configure',N'accounting.configure',N'Administrar plan, centros y mapeos contables'),
    (N'Manage',N'accounting.periods.manage',N'Abrir y cerrar periodos contables'),
    (N'Retry',N'accounting.postings.retry',N'Reintentar contabilizaciones pendientes');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Accounting',p.Action,p.Resource,p.Description,SYSUTCDATETIME() FROM @AccountingPermissions p
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r JOIN dbo.Permissions p ON p.Resource IN
  (N'accounting.read',N'accounting.configure',N'accounting.periods.manage',N'accounting.postings.retry')
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
