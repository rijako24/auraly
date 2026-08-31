IF OBJECT_ID(N'dbo.EnrolledDevices', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PosDevicePermissions', N'U') IS NOT NULL
BEGIN
    UPDATE permission
    SET permission.IsGranted = 1,
        permission.GrantedAt = SYSUTCDATETIME()
    FROM dbo.PosDevicePermissions permission
    INNER JOIN dbo.EnrolledDevices device ON device.DeviceId = permission.DeviceId
    WHERE device.IsActive = 1
      AND permission.PermissionCode IN (N'inventory.read', N'businesses.read')
      AND permission.IsGranted = 0;

    INSERT dbo.PosDevicePermissions(DeviceId, PermissionCode, IsGranted, GrantedAt)
    SELECT device.DeviceId, required.PermissionCode, 1, SYSUTCDATETIME()
    FROM dbo.EnrolledDevices device
    CROSS JOIN (VALUES (N'inventory.read'), (N'businesses.read')) required(PermissionCode)
    WHERE device.IsActive = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.PosDevicePermissions existing
          WHERE existing.DeviceId = device.DeviceId
            AND existing.PermissionCode = required.PermissionCode
      );
END;
GO
