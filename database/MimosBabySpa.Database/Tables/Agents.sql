CREATE TABLE [dbo].[Agents] (
    [AgentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AgentTypeId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [SettingsJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Agents_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Agents_AgentTypes] FOREIGN KEY ([AgentTypeId])
        REFERENCES [dbo].[AgentTypes] ([AgentTypeId])
        ON DELETE NO ACTION,
    CONSTRAINT [UQ_Agents_BusinessName] UNIQUE ([BusinessId], [Name])
);

GO

CREATE INDEX [IX_Agents_BusinessId] ON [dbo].[Agents] ([BusinessId]);

GO
