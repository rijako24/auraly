CREATE TABLE [dbo].[OfflineAuthenticationLeases]
(
    [LeaseId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [KeyId] NVARCHAR(64) NOT NULL,
    [Algorithm] NVARCHAR(16) NOT NULL,
    [SignedPayload] NVARCHAR(MAX) NOT NULL,
    [Signature] NVARCHAR(MAX) NOT NULL,
    [Nonce] UNIQUEIDENTIFIER NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [NotBefore] DATETIMEOFFSET(7) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [EndedAt] DATETIMEOFFSET(7) NULL,
    [EndReason] NVARCHAR(64) NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_OfflineAuthenticationLeases]
        PRIMARY KEY CLUSTERED ([LeaseId]),
    CONSTRAINT [FK_OfflineAuthenticationLeases_Tenants]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_OfflineAuthenticationLeases_Users]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_OfflineAuthenticationLeases_Devices]
        FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [UQ_OfflineAuthenticationLeases_Nonce] UNIQUE ([Nonce]),
    CONSTRAINT [CK_OfflineAuthenticationLeases_Algorithm]
        CHECK ([Algorithm]=N'PS256'),
    CONSTRAINT [CK_OfflineAuthenticationLeases_Status]
        CHECK ([Status] IN (N'Active',N'Released',N'Revoked',N'Expired')),
    CONSTRAINT [CK_OfflineAuthenticationLeases_Dates]
        CHECK ([IssuedAt]<=[NotBefore] AND [NotBefore]<[ExpiresAt]),
    CONSTRAINT [CK_OfflineAuthenticationLeases_End]
        CHECK (
            ([Status]=N'Active' AND [EndedAt] IS NULL AND [EndReason] IS NULL)
            OR
            ([Status]<>N'Active' AND [EndedAt] IS NOT NULL AND [EndReason] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_OfflineAuthenticationLeases_User_Active]
    ON [dbo].[OfflineAuthenticationLeases] ([TenantId],[UserId])
    WHERE [Status]=N'Active';
GO

CREATE UNIQUE INDEX [UX_OfflineAuthenticationLeases_Device_Active]
    ON [dbo].[OfflineAuthenticationLeases] ([DeviceId])
    WHERE [Status]=N'Active';
GO

CREATE INDEX [IX_OfflineAuthenticationLeases_Expiration]
    ON [dbo].[OfflineAuthenticationLeases] ([Status],[ExpiresAt]);
GO

CREATE INDEX [IX_OfflineAuthenticationLeases_History]
    ON [dbo].[OfflineAuthenticationLeases] ([TenantId],[UserId],[IssuedAt] DESC);
GO
