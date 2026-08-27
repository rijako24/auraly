SET NOCOUNT ON;
DECLARE @PayrollPermissions TABLE([Action] NVARCHAR(50),[Resource] NVARCHAR(100),[Description] NVARCHAR(500));
INSERT @PayrollPermissions VALUES
 (N'Read',N'payroll.read',N'Consultar relaciones laborales, liquidaciones y comprobantes'),
 (N'Manage',N'payroll.manage',N'Administrar contratos, conceptos, novedades y deducciones'),
 (N'Calculate',N'payroll.calculate',N'Calcular borradores de nómina'),
 (N'Approve',N'payroll.approve',N'Aprobar liquidaciones de nómina'),
 (N'Pay',N'payroll.pay',N'Confirmar pagos de nómina'),
 (N'Configure',N'payroll.configure',N'Configurar reglas y mapeos de nómina'),
 (N'Fiscal',N'payroll.fiscal',N'Generar y ajustar nómina electrónica');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Payroll',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @PayrollPermissions p WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r CROSS JOIN dbo.Permissions p
WHERE r.IsActive=1 AND r.NormalizedName IN(N'ADMINISTRATOR',N'SUPERADMIN')
 AND p.Resource IN(N'payroll.read',N'payroll.manage',N'payroll.calculate',N'payroll.approve',N'payroll.pay',N'payroll.configure',N'payroll.fiscal')
 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
