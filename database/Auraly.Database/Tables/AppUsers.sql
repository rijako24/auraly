CREATE TABLE [dbo].[AppUsers] (
    [UserId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NULL,
    [Username] NVARCHAR(100) NOT NULL,
    [NormalizedUsername] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(256) NOT NULL,
    [NormalizedEmail] NVARCHAR(256) NOT NULL,
    [PasswordHash] NVARCHAR(500) NULL,
    [PosOfflinePasswordSalt] VARBINARY(16) NULL,
    [PosOfflinePasswordHash] VARBINARY(32) NULL,
    [PosOfflinePasswordIterations] INT NULL,
    [PosOfflinePasswordChangedAt] DATETIMEOFFSET(7) NULL,
    [FirstName] NVARCHAR(100) NOT NULL,
    [LastName] NVARCHAR(100) NOT NULL,
    [PhoneNumber] NVARCHAR(20) NULL,
    [AvatarUrl] NVARCHAR(500) NULL,
    [AccessFailedCount] INT NOT NULL DEFAULT 0,
    [EmailConfirmed] BIT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [LastLoginAt] DATETIME2 NULL,
    [LockoutEnd] DATETIMEOFFSET NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_AppUsers_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_AppUsers_Parties] FOREIGN KEY ([TenantId], [PartyId])
        REFERENCES [dbo].[Parties] ([TenantId], [PartyId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_AppUsers_CreatedByUser] FOREIGN KEY ([CreatedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_AppUsers_PosOfflinePasswordComplete] CHECK (
        ([PosOfflinePasswordSalt] IS NULL
         AND [PosOfflinePasswordHash] IS NULL
         AND [PosOfflinePasswordIterations] IS NULL
         AND [PosOfflinePasswordChangedAt] IS NULL)
        OR
        ([PosOfflinePasswordSalt] IS NOT NULL
         AND [PosOfflinePasswordHash] IS NOT NULL
         AND [PosOfflinePasswordIterations] >= 100000
         AND [PosOfflinePasswordChangedAt] IS NOT NULL))
);

GO

CREATE UNIQUE INDEX [UX_AppUsers_Tenant_Username] ON [dbo].[AppUsers] ([TenantId], [NormalizedUsername]);

GO

CREATE UNIQUE INDEX [UX_AppUsers_Tenant_Email] ON [dbo].[AppUsers] ([TenantId], [NormalizedEmail]);

GO

CREATE INDEX [IX_AppUsers_TenantId] ON [dbo].[AppUsers] ([TenantId]);

GO

CREATE UNIQUE INDEX [UX_AppUsers_PartyId] ON [dbo].[AppUsers] ([PartyId])
    WHERE [PartyId] IS NOT NULL;

GO

CREATE INDEX [IX_AppUsers_CreatedByUserId] ON [dbo].[AppUsers] ([CreatedByUserId]);

GO
