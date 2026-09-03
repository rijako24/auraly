SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.WorkSessions', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.WorkSessions', N'TenantId') IS NULL
        ALTER TABLE dbo.WorkSessions ADD TenantId UNIQUEIDENTIFIER NULL;

    EXEC sys.sp_executesql N'
        UPDATE session
        SET TenantId = business.TenantId
        FROM dbo.WorkSessions session
        INNER JOIN dbo.Businesses business ON business.BusinessId = session.BusinessId
        WHERE session.TenantId IS NULL;';

    DECLARE @UnscopedSessions BIGINT;
    EXEC sys.sp_executesql
      N'SELECT @Count=COUNT_BIG(*) FROM dbo.WorkSessions WHERE TenantId IS NULL;',
      N'@Count BIGINT OUTPUT', @Count=@UnscopedSessions OUTPUT;
    IF @UnscopedSessions > 0
        THROW 51620, 'Every work session must resolve to its business tenant before enforcing tenant scope.', 1;

    EXEC sys.sp_executesql N'ALTER TABLE dbo.WorkSessions ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Businesses') AND name=N'UQ_Businesses_Business_Tenant')
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UQ_Businesses_Business_Tenant
          ON dbo.Businesses(BusinessId,TenantId);';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AppUsers') AND name=N'UQ_AppUsers_User_Tenant')
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UQ_AppUsers_User_Tenant
          ON dbo.AppUsers(UserId,TenantId);';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.EnrolledDevices') AND name=N'UQ_EnrolledDevices_Device_Tenant')
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UQ_EnrolledDevices_Device_Tenant
          ON dbo.EnrolledDevices(DeviceId,TenantId);';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'UQ_WorkSessions_Session_Business')
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UQ_WorkSessions_Session_Business
          ON dbo.WorkSessions(WorkSessionId,BusinessId);';

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.WorkSessions')
          AND name = N'FK_WorkSessions_Tenants')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.WorkSessions WITH CHECK
          ADD CONSTRAINT FK_WorkSessions_Tenants FOREIGN KEY(TenantId)
          REFERENCES dbo.Tenants(TenantId);';

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.WorkSessions')
          AND name = N'FK_WorkSessions_BusinessTenant')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.WorkSessions WITH CHECK
          ADD CONSTRAINT FK_WorkSessions_BusinessTenant FOREIGN KEY(BusinessId,TenantId)
          REFERENCES dbo.Businesses(BusinessId,TenantId);';

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.WorkSessions')
          AND name = N'FK_WorkSessions_BusinessWarehouse')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.WorkSessions WITH CHECK
          ADD CONSTRAINT FK_WorkSessions_BusinessWarehouse FOREIGN KEY(BusinessId,WarehouseId)
          REFERENCES dbo.Warehouses(BusinessId,WarehouseId);';

    -- Historical sessions created before tenant scoping remain auditable. Invalid
    -- open contexts are retired before the trusted boundary is enforced for every
    -- new write; no user can continue operating a session owned by another tenant.
    EXEC sys.sp_executesql N'
        UPDATE sessionValue
        SET Status=N''Closed'',
            ClosedAt=COALESCE(sessionValue.ClosedAt,SYSDATETIMEOFFSET()),
            LastActivityAt=SYSDATETIMEOFFSET()
        FROM dbo.WorkSessions sessionValue
        INNER JOIN dbo.AppUsers userValue ON userValue.UserId=sessionValue.UserId
        WHERE sessionValue.TenantId<>userValue.TenantId
          AND sessionValue.Status=N''Open'';';

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.WorkSessions')
          AND name = N'FK_WorkSessions_UserTenant')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.WorkSessions WITH NOCHECK
          ADD CONSTRAINT FK_WorkSessions_UserTenant FOREIGN KEY(UserId,TenantId)
          REFERENCES dbo.AppUsers(UserId,TenantId);';

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.WorkSessions')
          AND name = N'FK_WorkSessions_DeviceTenant')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.WorkSessions WITH NOCHECK
          ADD CONSTRAINT FK_WorkSessions_DeviceTenant FOREIGN KEY(DeviceId,TenantId)
          REFERENCES dbo.EnrolledDevices(DeviceId,TenantId);';

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'UX_WorkSessions_User_Open')
        DROP INDEX UX_WorkSessions_User_Open ON dbo.WorkSessions;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'UX_WorkSessions_Tenant_User_Open')
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_WorkSessions_Tenant_User_Open
          ON dbo.WorkSessions(TenantId,UserId) WHERE Status=N''Open'';';
END;

COMMIT TRANSACTION;
