CREATE TABLE [dbo].[Permissions] (
    [PermissionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [Module] NVARCHAR(50) NOT NULL,
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

GO

CREATE UNIQUE INDEX [IX_Permissions_Resource] ON [dbo].[Permissions] ([Resource]);

GO
