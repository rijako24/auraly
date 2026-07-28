CREATE TABLE [dbo].[AppRoles] (
    [RoleId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [NormalizedName] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [IsSystemRole] BIT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_AppRoles_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_AppRoles_TenantId_NormalizedName] ON [dbo].[AppRoles] ([TenantId], [NormalizedName])
    WHERE [TenantId] IS NOT NULL;

GO
