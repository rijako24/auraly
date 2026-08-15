CREATE TABLE [dbo].[PasswordResetRequests]
(
    [PasswordResetRequestId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] VARBINARY(32) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UsedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_PasswordResetRequests] PRIMARY KEY ([PasswordResetRequestId]),
    CONSTRAINT [FK_PasswordResetRequests_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PasswordResetRequests_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [UQ_PasswordResetRequests_TokenHash] UNIQUE ([TokenHash]),
    CONSTRAINT [CK_PasswordResetRequests_Status] CHECK ([Status] IN (N'Pending',N'Used',N'Expired',N'Revoked'))
);
GO
CREATE INDEX [IX_PasswordResetRequests_User_Status]
    ON [dbo].[PasswordResetRequests]([TenantId],[UserId],[Status],[CreatedAt] DESC);
GO