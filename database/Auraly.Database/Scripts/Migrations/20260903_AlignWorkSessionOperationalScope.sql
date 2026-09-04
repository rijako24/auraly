SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.WorkSessions', N'U') IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.WorkSessions')
          AND name = N'WarehouseId'
          AND is_nullable = 0
    )
        ALTER TABLE dbo.WorkSessions
            ALTER COLUMN WarehouseId UNIQUEIDENTIFIER NULL;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'UX_WorkSessions_Tenant_User_Open')
        DROP INDEX UX_WorkSessions_Tenant_User_Open ON dbo.WorkSessions;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'UX_WorkSessions_Device_Open')
        DROP INDEX UX_WorkSessions_Device_Open ON dbo.WorkSessions;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'IX_WorkSessions_Business_Warehouse_Opened')
        DROP INDEX IX_WorkSessions_Business_Warehouse_Opened ON dbo.WorkSessions;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'UX_WorkSessions_Web_User_Open')
        CREATE UNIQUE INDEX UX_WorkSessions_Web_User_Open
            ON dbo.WorkSessions(TenantId,BusinessId,UserId)
            WHERE Status=N'Open' AND DeviceId IS NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'UX_WorkSessions_Device_User_Open')
        CREATE UNIQUE INDEX UX_WorkSessions_Device_User_Open
            ON dbo.WorkSessions(TenantId,BusinessId,DeviceId,UserId)
            WHERE Status=N'Open' AND DeviceId IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WorkSessions') AND name=N'IX_WorkSessions_Business_Opened')
        CREATE INDEX IX_WorkSessions_Business_Opened
            ON dbo.WorkSessions(TenantId,BusinessId,OpenedAt DESC);
END;

COMMIT TRANSACTION;
