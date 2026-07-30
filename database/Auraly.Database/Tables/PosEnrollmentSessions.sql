CREATE TABLE [dbo].[PosEnrollmentSessions]
(
    [EnrollmentSessionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [RequestedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [RequestedByDisplayName] NVARCHAR(200) NOT NULL,
    [DeviceName] NVARCHAR(160) NOT NULL,
    [RedemptionCodeHash] VARBINARY(32) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [RedeemedAt] DATETIMEOFFSET(7) NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PosEnrollmentSessions] PRIMARY KEY CLUSTERED ([EnrollmentSessionId]),
    CONSTRAINT [FK_PosEnrollmentSessions_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_PosEnrollmentSessions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_PosEnrollmentSessions_Warehouses] FOREIGN KEY ([WarehouseId])
        REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_PosEnrollmentSessions_Registers] FOREIGN KEY ([RegisterId])
        REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [FK_PosEnrollmentSessions_Users] FOREIGN KEY ([RequestedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_PosEnrollmentSessions_Devices] FOREIGN KEY ([DeviceId])
        REFERENCES [dbo].[PosDevices] ([DeviceId]),
    CONSTRAINT [CK_PosEnrollmentSessions_Expiry] CHECK ([ExpiresAt] > [CreatedAt]),
    CONSTRAINT [CK_PosEnrollmentSessions_Redeemed] CHECK (
        ([RedeemedAt] IS NULL AND [DeviceId] IS NULL) OR
        ([RedeemedAt] IS NOT NULL AND [DeviceId] IS NOT NULL))
);

GO

CREATE INDEX [IX_PosEnrollmentSessions_Register_Expiry]
    ON [dbo].[PosEnrollmentSessions] ([RegisterId], [ExpiresAt], [RedeemedAt]);

GO

CREATE UNIQUE INDEX [UX_PosEnrollmentSessions_CodeHash]
    ON [dbo].[PosEnrollmentSessions] ([RedemptionCodeHash]);
