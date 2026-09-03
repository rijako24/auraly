CREATE TABLE [dbo].[WorkSessions]
(
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [OpenedAt] DATETIMEOFFSET(7) NOT NULL,
    [LastActivityAt] DATETIMEOFFSET(7) NOT NULL,
    [ClosedAt] DATETIMEOFFSET(7) NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_WorkSessions] PRIMARY KEY CLUSTERED ([WorkSessionId]),
    CONSTRAINT [FK_WorkSessions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_WorkSessions_BusinessTenant] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_WorkSessions_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_WorkSessions_Warehouses] FOREIGN KEY ([WarehouseId])
        REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_WorkSessions_BusinessWarehouse] FOREIGN KEY ([BusinessId],[WarehouseId])
        REFERENCES [dbo].[Warehouses] ([BusinessId],[WarehouseId]),
    CONSTRAINT [FK_WorkSessions_Users] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_WorkSessions_UserTenant] FOREIGN KEY ([UserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_WorkSessions_Devices] FOREIGN KEY ([DeviceId])
        REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [FK_WorkSessions_DeviceTenant] FOREIGN KEY ([DeviceId],[TenantId])
        REFERENCES [dbo].[EnrolledDevices] ([DeviceId],[TenantId]),
    CONSTRAINT [UQ_WorkSessions_Session_Business] UNIQUE ([WorkSessionId],[BusinessId]),
    CONSTRAINT [CK_WorkSessions_Status] CHECK (
        ([Status]=N'Open' AND [ClosedAt] IS NULL)
        OR ([Status]=N'Closed' AND [ClosedAt] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_WorkSessions_Tenant_User_Open]
    ON [dbo].[WorkSessions] ([TenantId],[UserId]) WHERE [Status]=N'Open';
GO

CREATE UNIQUE INDEX [UX_WorkSessions_Device_Open]
    ON [dbo].[WorkSessions] ([DeviceId], [BusinessId])
    WHERE [Status]=N'Open' AND [DeviceId] IS NOT NULL;
GO

CREATE INDEX [IX_WorkSessions_Business_Warehouse_Opened]
    ON [dbo].[WorkSessions] ([BusinessId],[WarehouseId],[OpenedAt] DESC);
GO
