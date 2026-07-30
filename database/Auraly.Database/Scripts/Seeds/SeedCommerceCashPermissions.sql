SET NOCOUNT ON;

DECLARE @CashPermissions TABLE
(
    [Module] NVARCHAR(50) NOT NULL,
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL
);

INSERT INTO @CashPermissions ([Module],[Action],[Resource],[Description])
VALUES
    (N'Cash',N'Read',N'cash.read',N'Consultar sesiones y arqueos de caja'),
    (N'Cash',N'Open',N'cash.open',N'Abrir una sesión física de caja'),
    (N'Cash',N'Count',N'cash.count',N'Realizar conteos de caja'),
    (N'Cash',N'ApproveHandoff',N'cash.handoff.approve',N'Autorizar una entrega de caja'),
    (N'Cash',N'Close',N'cash.close',N'Cerrar una sesión física de caja'),
    (N'Security',N'ManageSupervisorCredentials',N'security.supervisor-credentials.manage',
     N'Crear o rotar credenciales imprimibles de supervisor');

INSERT dbo.Permissions
    (PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),p.Module,p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @CashPermissions p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);

INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource IN
(
    N'cash.read',
    N'cash.open',
    N'cash.count',
    N'cash.handoff.approve',
    N'cash.close',
    N'security.supervisor-credentials.manage'
)
WHERE r.IsActive=1
  AND r.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId
  );
