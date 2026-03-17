CREATE TABLE [dbo].[BusinessConfigurations] (
    [BusinessConfigurationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Key] INT NOT NULL,
    [Value] NVARCHAR(MAX) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_BusinessConfigurations_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE CASCADE
);

GO

CREATE UNIQUE INDEX [IX_BusinessConfigurations_BusinessId_Key] ON [dbo].[BusinessConfigurations] ([BusinessId], [Key]);

GO

CREATE INDEX [IX_BusinessConfigurations_BusinessId] ON [dbo].[BusinessConfigurations] ([BusinessId]);

GO
