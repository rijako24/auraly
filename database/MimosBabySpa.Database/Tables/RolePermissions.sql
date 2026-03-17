CREATE TABLE [dbo].[RolePermissions] (
    [RolePermissionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [AssignedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_RolePermissions_AppRoles] FOREIGN KEY ([RoleId])
        REFERENCES [dbo].[AppRoles] ([RoleId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_RolePermissions_Permissions] FOREIGN KEY ([PermissionId])
        REFERENCES [dbo].[Permissions] ([PermissionId])
        ON DELETE CASCADE
);

GO

CREATE UNIQUE INDEX [IX_RolePermissions_RoleId_PermissionId] ON [dbo].[RolePermissions] ([RoleId], [PermissionId]);

GO

CREATE INDEX [IX_RolePermissions_RoleId] ON [dbo].[RolePermissions] ([RoleId]);

GO

CREATE INDEX [IX_RolePermissions_PermissionId] ON [dbo].[RolePermissions] ([PermissionId]);

GO
