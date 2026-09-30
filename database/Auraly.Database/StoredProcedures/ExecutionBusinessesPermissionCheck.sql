CREATE PROCEDURE dbo.ExecutionBusinessesPermissionCheck
    @UserId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER,
    @BusinessIdsJson NVARCHAR(MAX),
    @Permission NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.AppUsers WHERE UserId=@UserId AND IsActive=1)
    BEGIN
        SELECT 0;
        RETURN;
    END;
    DECLARE @CanReadAll BIT=CASE WHEN EXISTS(
      SELECT 1 FROM dbo.AppUsers userValue
      JOIN dbo.Tenants identityTenant ON identityTenant.TenantId=userValue.TenantId AND identityTenant.TenantKey=N'@auraly'
      JOIN dbo.UserRoles userRole ON userRole.UserId=userValue.UserId
      JOIN dbo.AppRoles roleValue ON roleValue.RoleId=userRole.RoleId AND roleValue.IsActive=1
      JOIN dbo.RolePermissions rolePermission ON rolePermission.RoleId=roleValue.RoleId
      JOIN dbo.Permissions permissionValue ON permissionValue.PermissionId=rolePermission.PermissionId
      WHERE userValue.UserId=@UserId AND userValue.IsActive=1
        AND permissionValue.Resource=N'tenants.read') THEN 1 ELSE 0 END;
    SELECT COUNT(*) FROM OPENJSON(@BusinessIdsJson)
      WITH (BusinessId UNIQUEIDENTIFIER '$') requested
    JOIN dbo.Businesses businessValue
      ON businessValue.BusinessId=requested.BusinessId
     AND businessValue.TenantId=@TenantId AND businessValue.IsActive=1
    WHERE @CanReadAll=1 OR EXISTS(
      SELECT 1 FROM dbo.UserRoles userRole
      JOIN dbo.AppRoles roleValue ON roleValue.RoleId=userRole.RoleId AND roleValue.IsActive=1
      JOIN dbo.RolePermissions rolePermission ON rolePermission.RoleId=roleValue.RoleId
      JOIN dbo.Permissions permissionValue ON permissionValue.PermissionId=rolePermission.PermissionId
      LEFT JOIN dbo.Businesses roleBusiness ON roleBusiness.BusinessId=userRole.BusinessId
      WHERE userRole.UserId=@UserId AND permissionValue.Resource=@Permission
        AND (roleValue.TenantId=@TenantId OR roleValue.TenantId IS NULL
             OR roleBusiness.TenantId=@TenantId)
        AND (userRole.BusinessId IS NULL OR userRole.BusinessId=businessValue.BusinessId));
END;
