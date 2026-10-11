CREATE PROCEDURE [dbo].[RestaurantWaiterRoleEnsure]
    @TenantId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT=0
        THROW 51481, 'Restaurant role provisioning requires the caller transaction.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Resource=N'restaurant.attend')
        THROW 51480, 'The restaurant attendance permission must be provisioned first.', 1;

    DECLARE @CreatedRoles TABLE (RoleId UNIQUEIDENTIFIER PRIMARY KEY);

    INSERT dbo.AppRoles
        (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
    OUTPUT inserted.RoleId INTO @CreatedRoles(RoleId)
    SELECT NEWID(),tenant.TenantId,N'Mesero',N'WAITER',
           N'Atención de mesas y toma de consumos, sin facturación ni operaciones de caja.',
           1,0,SYSUTCDATETIME()
    FROM dbo.Tenants tenant
    WHERE (@TenantId IS NULL OR tenant.TenantId=@TenantId)
      AND EXISTS (SELECT 1 FROM dbo.Businesses business
          WHERE business.TenantId=tenant.TenantId AND business.IsActive=1
            AND business.PointOfSaleMode=N'RestaurantBar')
      AND NOT EXISTS (
          SELECT 1 FROM dbo.AppRoles existing WITH (UPDLOCK,HOLDLOCK)
          WHERE existing.TenantId=tenant.TenantId AND existing.NormalizedName=N'WAITER')
    OPTION (RECOMPILE);

    -- Only initialize roles created by this invocation. Never restore removed
    -- permissions or touch another role/user when the deployment is repeated.
    INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
    SELECT NEWID(),created.RoleId,permission.PermissionId,SYSUTCDATETIME()
    FROM @CreatedRoles created
    JOIN dbo.Permissions permission ON permission.Resource=N'restaurant.attend';

END;
