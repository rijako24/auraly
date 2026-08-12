SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Resource=N'pos.devices.enroll')
BEGIN
    INSERT dbo.Permissions
        (PermissionId,Module,Action,Resource,Description,CreatedAt)
    VALUES
        (NEWID(),N'POS',N'EnrollDevice',N'pos.devices.enroll',
         N'Autorizar el enrolamiento offline de una caja',SYSUTCDATETIME());
END;

INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource=N'pos.devices.enroll'
WHERE r.IsActive=1
  AND r.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.RolePermissions rp
      WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId
  );
