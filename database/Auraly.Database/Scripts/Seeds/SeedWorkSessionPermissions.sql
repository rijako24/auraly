SET NOCOUNT ON;

DECLARE @WorkSessionPermissions TABLE
(
    [Module] NVARCHAR(50) NOT NULL,
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL
);

INSERT @WorkSessionPermissions ([Module],[Action],[Resource],[Description])
VALUES
    (N'WorkSessions',N'Read',N'work-sessions.read',N'Consultar la sesión de trabajo propia'),
    (N'WorkSessions',N'Open',N'work-sessions.open',N'Abrir o recuperar la sesión de trabajo propia'),
    (N'WorkSessions',N'Close',N'work-sessions.close',N'Cerrar y conciliar la sesión de trabajo propia'),
    (N'WorkSessions',N'ReadDifferences',N'work-sessions.differences.read',N'Consultar cierres y sus diferencias por medio de pago'),
    (N'WorkSessions',N'ReconcileClosures',N'work-sessions.closures.reconcile',N'Conciliar cierres y reclasificar medios de pago');

INSERT dbo.Permissions
    (PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),p.Module,p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @WorkSessionPermissions p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);

INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource IN
(
    N'work-sessions.read',
    N'work-sessions.open',
    N'work-sessions.close',
    N'work-sessions.differences.read',
    N'work-sessions.closures.reconcile'
)
WHERE r.IsActive=1
  AND r.NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR')
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId
  );
INSERT dbo.Permissions
    (PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'WorkSessions',v.Action,v.Resource,v.Description,SYSUTCDATETIME()
FROM (VALUES
    (N'ManageCash',N'work-sessions.cash.manage',
     N'Registrar entradas y salidas de efectivo en la caja propia'),
    (N'OpenCashDrawer',N'work-sessions.cash.drawer.open',
     N'Abrir manualmente el cajón de dinero desde el punto de venta'),
    (N'ConfigureCashReasons',N'work-sessions.cash-reasons.configure',
     N'Configurar conceptos contables de entradas y salidas de caja')
) v(Action,Resource,Description)
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.Permissions p WHERE p.Resource=v.Resource
);

INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource IN
(
    N'work-sessions.cash.manage',
    N'work-sessions.cash.drawer.open',
    N'work-sessions.cash-reasons.configure'
)
WHERE r.IsActive=1 AND r.NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR')
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId
  );
