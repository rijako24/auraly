CREATE TABLE [dbo].[AppUsers] (
    [UserId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NULL,
    [Username] NVARCHAR(100) NOT NULL,
    [NormalizedUsername] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(256) NOT NULL,
    [NormalizedEmail] NVARCHAR(256) NOT NULL,
    [PasswordHash] NVARCHAR(500) NULL,
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
    CONSTRAINT [FK_AppUsers_CreatedByUser] FOREIGN KEY ([CreatedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_AppUsers_NormalizedUsername] ON [dbo].[AppUsers] ([NormalizedUsername]);

GO

CREATE UNIQUE INDEX [IX_AppUsers_NormalizedEmail] ON [dbo].[AppUsers] ([NormalizedEmail]);

GO

CREATE INDEX [IX_AppUsers_TenantId] ON [dbo].[AppUsers] ([TenantId]);

GO

CREATE INDEX [IX_AppUsers_CreatedByUserId] ON [dbo].[AppUsers] ([CreatedByUserId]);

GO
