CREATE TABLE [dbo].[EnrolledDevices]
(
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [CredentialSalt] VARBINARY(32) NOT NULL,
    [CredentialHash] VARBINARY(32) NOT NULL,
    [CredentialIterations] INT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_EnrolledDevices_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [LastSeenAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_EnrolledDevices] PRIMARY KEY CLUSTERED ([DeviceId]),
    CONSTRAINT [FK_EnrolledDevices_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [CK_EnrolledDevices_CredentialIterations] CHECK ([CredentialIterations] >= 100000)
);
GO

CREATE INDEX [IX_EnrolledDevices_Tenant_Active]
    ON [dbo].[EnrolledDevices] ([TenantId], [IsActive], [Name]);
GO