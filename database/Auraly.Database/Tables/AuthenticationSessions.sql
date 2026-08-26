CREATE TABLE [dbo].[AuthenticationSessions]
(
    [AuthenticationSessionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ClientId] UNIQUEIDENTIFIER NOT NULL,
    [ClientDescription] NVARCHAR(500) NULL,
    [IpAddress] NVARCHAR(64) NULL,
    [RefreshTokenHash] VARBINARY(32) NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [LastSeenAt] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [RevokedAt] DATETIMEOFFSET(7) NULL,
    [RevocationReason] NVARCHAR(64) NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AuthenticationSessions]
        PRIMARY KEY CLUSTERED ([AuthenticationSessionId]),
    CONSTRAINT [FK_AuthenticationSessions_Tenants]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_AuthenticationSessions_Users]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_AuthenticationSessions_RefreshTokenHash]
        UNIQUE ([RefreshTokenHash]),
    CONSTRAINT [CK_AuthenticationSessions_Status]
        CHECK ([Status] IN (N'Active',N'Revoked',N'Expired')),
    CONSTRAINT [CK_AuthenticationSessions_Expiration]
        CHECK ([ExpiresAt]>[IssuedAt]),
    CONSTRAINT [CK_AuthenticationSessions_Revocation]
        CHECK (
            ([Status]=N'Active' AND [RevokedAt] IS NULL AND [RevocationReason] IS NULL)
            OR
            ([Status] IN (N'Revoked',N'Expired')
             AND [RevokedAt] IS NOT NULL AND [RevocationReason] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_AuthenticationSessions_User_Client_Active]
    ON [dbo].[AuthenticationSessions] ([TenantId],[UserId],[ClientId])
    WHERE [Status]=N'Active';
GO

CREATE INDEX [IX_AuthenticationSessions_User_History]
    ON [dbo].[AuthenticationSessions] ([TenantId],[UserId],[IssuedAt] DESC);
GO

CREATE INDEX [IX_AuthenticationSessions_Expires]
    ON [dbo].[AuthenticationSessions] ([Status],[ExpiresAt]);
GO
