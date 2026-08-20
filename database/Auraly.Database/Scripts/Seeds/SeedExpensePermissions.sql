SET NOCOUNT ON;
DECLARE @ExpensePermissions TABLE([Action] NVARCHAR(50),[Resource] NVARCHAR(100),[Description] NVARCHAR(500));
INSERT @ExpensePermissions VALUES
 (N'Read',N'expenses.read',N'Consultar gastos y sus reportes'),
 (N'Create',N'expenses.create',N'Registrar y confirmar gastos'),
 (N'Configure',N'expenses.configure',N'Configurar conceptos y cuentas de gasto');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Expenses',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @ExpensePermissions p WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r CROSS JOIN dbo.Permissions p
WHERE r.IsActive=1 AND r.NormalizedName IN(N'ADMINISTRATOR',N'SUPERADMIN')
 AND p.Resource IN(N'expenses.read',N'expenses.create',N'expenses.configure')
 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
