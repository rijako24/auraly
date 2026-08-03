SET NOCOUNT ON;
DECLARE @ReceivablesPermissions TABLE([Action] NVARCHAR(50) NOT NULL,[Resource] NVARCHAR(100) NOT NULL,[Description] NVARCHAR(500) NOT NULL);
INSERT @ReceivablesPermissions VALUES
(N'Read',N'receivables.read',N'Consultar cartera y movimientos de clientes'),
(N'Create',N'receivables.payments.create',N'Registrar recaudos aplicados a cartera'),
(N'Manage',N'receivables.credit.manage',N'Configurar condiciones y cupo de cr?dito');
INSERT dbo.Permissions(PermissionId,[Module],[Action],[Resource],[Description],CreatedAt)
SELECT NEWID(),N'Receivables',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @ReceivablesPermissions p
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource IN
    (N'receivables.read',N'receivables.payments.create',N'receivables.credit.manage')
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp
    WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
