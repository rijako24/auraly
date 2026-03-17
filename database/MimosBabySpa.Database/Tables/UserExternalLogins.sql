CREATE TABLE [dbo].[UserExternalLogins] (
    [ExternalLoginId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Provider] NVARCHAR(50) NOT NULL,
    [ProviderKey] NVARCHAR(256) NOT NULL,
    [ProviderDisplayName] NVARCHAR(200) NULL,
    [ProviderEmail] NVARCHAR(256) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_UserExternalLogins_AppUsers] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE CASCADE
);

GO

CREATE UNIQUE INDEX [IX_UserExternalLogins_Provider_ProviderKey] ON [dbo].[UserExternalLogins] ([Provider], [ProviderKey]);

GO

CREATE INDEX [IX_UserExternalLogins_UserId] ON [dbo].[UserExternalLogins] ([UserId]);

GO
