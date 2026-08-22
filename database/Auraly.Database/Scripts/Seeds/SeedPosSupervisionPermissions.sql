DECLARE @Permissions TABLE(
    Module NVARCHAR(50) NOT NULL,
    Action NVARCHAR(50) NOT NULL,
    Resource NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NOT NULL);

INSERT @Permissions(Module,Action,Resource,Description)
VALUES
    (N'Sales',N'RemoveLine',N'sales.lines.remove',N'Retirar una línea de una venta en curso'),
    (N'Sales',N'RestartDraft',N'sales.drafts.restart',N'Reiniciar completamente una venta en curso'),
    (N'POS',N'AuthorizeSensitiveAction',N'pos.approvals.authorize',N'Aprobar acciones sensibles solicitadas desde el punto de venta'),
    (N'POS',N'ReadApprovals',N'pos.approvals.read',N'Consultar solicitudes de aprobación del punto de venta'),
    (N'POS',N'ReceiveApprovalNotifications',N'pos.approvals.receive_notifications',N'Recibir notificaciones remotas de solicitudes de aprobación POS'),
    (N'POS',N'ChangeSalesWorkspace',N'pos.workspace.change',N'Cambiar la sede y bodega activas de una caja en línea'),
    (N'POS',N'ManageApprovalCredential',N'pos.approvals.manage_credential',N'Crear, rotar o revocar la credencial secundaria de aprobación');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),source.Module,source.Action,source.Resource,source.Description,SYSUTCDATETIME()
FROM @Permissions source
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.Permissions existing WHERE existing.Resource=source.Resource);

/* Preserva el comportamiento de roles personalizados que ya tenían sales.void,
   pero lo divide en dos decisiones auditables. */
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(), legacy.RoleId, replacement.PermissionId, SYSUTCDATETIME()
FROM dbo.RolePermissions legacy
INNER JOIN dbo.Permissions previousPermission
    ON previousPermission.PermissionId=legacy.PermissionId
   AND previousPermission.Resource=N'sales.void'
CROSS JOIN dbo.Permissions replacement
WHERE replacement.Resource IN(N'sales.lines.remove',N'sales.drafts.restart')
  AND NOT EXISTS(
      SELECT 1 FROM dbo.RolePermissions currentAssignment
      WHERE currentAssignment.RoleId=legacy.RoleId
        AND currentAssignment.PermissionId=replacement.PermissionId);

/* Los roles estándar de administración y supervisión pueden atender solicitudes.
   El cajero no recibe estas concesiones por defecto. */
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(), roleValue.RoleId, permissionValue.PermissionId, SYSUTCDATETIME()
FROM dbo.AppRoles roleValue
CROSS JOIN dbo.Permissions permissionValue
WHERE roleValue.IsActive=1
  AND roleValue.NormalizedName IN(N'ADMINISTRATOR',N'TENANTADMINISTRATOR',N'ADMINISTRATIVE',N'SUPERVISOR')
  AND permissionValue.Resource IN(
      N'sales.lines.remove',N'sales.drafts.restart',
      N'pos.approvals.authorize',N'pos.approvals.read',N'pos.approvals.receive_notifications',N'pos.approvals.manage_credential',
      N'pos.workspace.change')
  AND NOT EXISTS(
      SELECT 1 FROM dbo.RolePermissions currentAssignment
      WHERE currentAssignment.RoleId=roleValue.RoleId
        AND currentAssignment.PermissionId=permissionValue.PermissionId);

DELETE assignment
FROM dbo.RolePermissions assignment
INNER JOIN dbo.Permissions permission ON permission.PermissionId=assignment.PermissionId
WHERE permission.Resource=N'sales.void';

DELETE FROM dbo.Permissions WHERE Resource=N'sales.void';
GO
