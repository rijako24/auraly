CREATE TABLE [dbo].[PosDevices]
(
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [LocationId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [CredentialSalt] VARBINARY(32) NOT NULL,
    [CredentialHash] VARBINARY(32) NOT NULL,
    [CredentialIterations] INT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_PosDevices_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [LastSeenAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_PosDevices] PRIMARY KEY CLUSTERED ([DeviceId]),
    CONSTRAINT [FK_PosDevices_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_PosDevices_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_PosDevices_BusinessLocations] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[BusinessLocations] ([LocationId]),
    CONSTRAINT [FK_PosDevices_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_PosDevices_CashRegisters] FOREIGN KEY ([RegisterId]) REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [CK_PosDevices_CredentialIterations] CHECK ([CredentialIterations] >= 100000)
);

GO

CREATE INDEX [IX_PosDevices_Tenant_Business_Register]
    ON [dbo].[PosDevices] ([TenantId], [BusinessId], [RegisterId]);

