CREATE TABLE [dbo].[FlowDefinitions] (
    [FlowDefinitionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [AgentId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [DefinitionJson] NVARCHAR(MAX) NOT NULL,
    [Version] NVARCHAR(20) NOT NULL DEFAULT N'1.0',
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_FlowDefinitions_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE CASCADE
);

GO

CREATE INDEX [IX_FlowDefinitions_AgentId] ON [dbo].[FlowDefinitions] ([AgentId]);

GO

CREATE INDEX [IX_FlowDefinitions_AgentActive] ON [dbo].[FlowDefinitions] ([AgentId], [IsActive]);

GO
