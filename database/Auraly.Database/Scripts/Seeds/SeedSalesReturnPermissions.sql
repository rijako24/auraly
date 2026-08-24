SET NOCOUNT ON;
DECLARE @Permissions TABLE
(
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL
);
INSERT @Permissions VALUES
    (N'Read',N'sales.returns.read',N'Consultar devoluciones de venta'),
    (N'Create',N'sales.returns.create',N'Crear devoluciones de venta'),
    (N'Confirm',N'sales.returns.confirm',N'Confirmar devoluciones de venta'),
    (N'Read',N'sales.debit-notes.read',N'Consultar notas débito de venta'),
    (N'Create',N'sales.debit-notes.create',N'Crear notas débito de venta'),
    (N'ReadReports',N'sales.reports.read',N'Consultar analítica y reportes de ventas');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Returns',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @Permissions p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource IN
  (N'sales.returns.read',N'sales.returns.create',N'sales.returns.confirm',
   N'sales.debit-notes.read',N'sales.debit-notes.create',N'sales.reports.read')
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
AND NOT EXISTS
(
    SELECT 1 FROM dbo.RolePermissions rp
    WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId
);
