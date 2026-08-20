SET NOCOUNT ON;

DECLARE @TransporterPermissions TABLE ([Resource] NVARCHAR(100) NOT NULL PRIMARY KEY);
INSERT @TransporterPermissions ([Resource]) VALUES
  (N'dispatches.read'),
  (N'dispatches.delivery.execute');

UPDATE dbo.AppRoles
SET IsSystemRole=0
WHERE NormalizedName IN(N'TRANSPORTADOR',N'TRANSPORTER',N'DRIVER');

DELETE assignment
FROM dbo.RolePermissions assignment
JOIN dbo.AppRoles roleValue ON roleValue.RoleId=assignment.RoleId
JOIN dbo.Permissions permissionValue ON permissionValue.PermissionId=assignment.PermissionId
WHERE roleValue.NormalizedName IN(N'TRANSPORTADOR',N'TRANSPORTER',N'DRIVER')
  AND permissionValue.Resource NOT IN (SELECT [Resource] FROM @TransporterPermissions);

INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),roleValue.RoleId,permissionValue.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles roleValue
JOIN dbo.Permissions permissionValue
  ON permissionValue.Resource IN (SELECT [Resource] FROM @TransporterPermissions)
WHERE roleValue.NormalizedName IN(N'TRANSPORTADOR',N'TRANSPORTER',N'DRIVER')
  AND roleValue.IsActive=1
  AND NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions assigned
    WHERE assigned.RoleId=roleValue.RoleId AND assigned.PermissionId=permissionValue.PermissionId);
GO
