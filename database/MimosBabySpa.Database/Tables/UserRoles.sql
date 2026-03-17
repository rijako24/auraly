CREATE TABLE [dbo].[UserRoles] (
    [UserRoleId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NULL,
    [AssignedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [AssignedByUserId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [FK_UserRoles_AppUsers] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_UserRoles_AppRoles] FOREIGN KEY ([RoleId])
        REFERENCES [dbo].[AppRoles] ([RoleId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_UserRoles_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_UserRoles_AssignedByUser] FOREIGN KEY ([AssignedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_UserRoles_UserId_RoleId] ON [dbo].[UserRoles] ([UserId], [RoleId])
    WHERE [BusinessId] IS NULL;

GO

CREATE UNIQUE INDEX [IX_UserRoles_UserId_RoleId_BusinessId] ON [dbo].[UserRoles] ([UserId], [RoleId], [BusinessId])
    WHERE [BusinessId] IS NOT NULL;

GO

CREATE INDEX [IX_UserRoles_UserId] ON [dbo].[UserRoles] ([UserId]);

GO

CREATE INDEX [IX_UserRoles_RoleId] ON [dbo].[UserRoles] ([RoleId]);

GO

CREATE INDEX [IX_UserRoles_BusinessId] ON [dbo].[UserRoles] ([BusinessId]);

GO

CREATE INDEX [IX_UserRoles_AssignedByUserId] ON [dbo].[UserRoles] ([AssignedByUserId]);

GO
