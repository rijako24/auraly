CREATE TABLE [dbo].[AgentKnowledgeSources] (
    [AgentKnowledgeSourceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [AgentId] UNIQUEIDENTIFIER NOT NULL,
    [KnowledgeSourceId] UNIQUEIDENTIFIER NOT NULL,
    [AutoInject] BIT NOT NULL DEFAULT 0,
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    CONSTRAINT [FK_AgentKnowledgeSources_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_AgentKnowledgeSources_KnowledgeSources] FOREIGN KEY ([KnowledgeSourceId])
        REFERENCES [dbo].[KnowledgeSources] ([KnowledgeSourceId])
        ON DELETE CASCADE,
    CONSTRAINT [UQ_AgentKnowledgeSources] UNIQUE ([AgentId], [KnowledgeSourceId])
);

GO
