CREATE TABLE [dbo].[WorkSessions]
(
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    -- Historical sessions retain their warehouse snapshot. New sessions are
    -- scoped by tenant, business, user and optional enrolled device; inventory
    -- documents continue to own and validate their WarehouseId independently.
    [WarehouseId] UNIQUEIDENTIFIER NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [Kind] NVARCHAR(16) NOT NULL CONSTRAINT [DF_WorkSessions_Kind] DEFAULT(N'Normal'),
    [DispatchId] UNIQUEIDENTIFIER NULL,
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
    CONSTRAINT [FK_WorkSessions_Dispatch] FOREIGN KEY ([DispatchId]) REFERENCES dbo.Dispatches(DispatchId),
    CONSTRAINT [CK_WorkSessions_Kind] CHECK ((Kind=N'Normal' AND DispatchId IS NULL)
        OR (Kind=N'Dispatch' AND DispatchId IS NOT NULL AND DeviceId IS NULL AND WarehouseId IS NULL)),
    CONSTRAINT [UQ_WorkSessions_Session_Business] UNIQUE ([WorkSessionId],[BusinessId]),
    CONSTRAINT [CK_WorkSessions_Status] CHECK (
        ([Status]=N'Open' AND [ClosedAt] IS NULL)
        OR ([Status]=N'Closed' AND [ClosedAt] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_WorkSessions_Web_User_Open]
    ON [dbo].[WorkSessions] ([TenantId],[BusinessId],[UserId])
    WHERE [Status]=N'Open' AND [DeviceId] IS NULL AND [Kind]=N'Normal';
GO

CREATE UNIQUE INDEX [UX_WorkSessions_Device_User_Open]
    ON [dbo].[WorkSessions] ([TenantId],[BusinessId],[DeviceId],[UserId])
    WHERE [Status]=N'Open' AND [DeviceId] IS NOT NULL;
GO

CREATE INDEX [IX_WorkSessions_Business_Opened]
    ON [dbo].[WorkSessions] ([TenantId],[BusinessId],[OpenedAt] DESC);
GO

CREATE UNIQUE INDEX [UX_WorkSessions_Dispatch] ON dbo.WorkSessions(DispatchId) WHERE DispatchId IS NOT NULL;
GO
