CREATE TABLE [dbo].[RefreshTokens] (
    [RefreshTokenId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Token] NVARCHAR(500) NOT NULL,
    [ExpiresAt] DATETIME2 NOT NULL,
    [DeviceInfo] NVARCHAR(500) NULL,
    [IpAddress] NVARCHAR(50) NULL,
    [ReplacedByTokenId] UNIQUEIDENTIFIER NULL,
    [RevokedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_RefreshTokens_AppUsers] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_RefreshTokens_ReplacedByToken] FOREIGN KEY ([ReplacedByTokenId])
        REFERENCES [dbo].[RefreshTokens] ([RefreshTokenId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_RefreshTokens_Token] ON [dbo].[RefreshTokens] ([Token]);

GO

CREATE INDEX [IX_RefreshTokens_UserId] ON [dbo].[RefreshTokens] ([UserId]);

GO

CREATE INDEX [IX_RefreshTokens_ExpiresAt] ON [dbo].[RefreshTokens] ([ExpiresAt]);

GO

CREATE INDEX [IX_RefreshTokens_ReplacedByTokenId] ON [dbo].[RefreshTokens] ([ReplacedByTokenId]);

GO
