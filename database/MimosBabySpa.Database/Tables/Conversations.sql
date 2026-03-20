CREATE TABLE [dbo].[Conversations] (
    [ConversationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AgentId] UNIQUEIDENTIFIER NULL,
    [UserNumber] NVARCHAR(50) NOT NULL,
    [LastMessage] NVARCHAR(1000) NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CustomerName] NVARCHAR(100) NULL,
    CONSTRAINT [FK_Conversations_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Conversations_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE SET NULL
);

GO

CREATE INDEX [IX_Conversations_BusinessId_UserNumber] ON [dbo].[Conversations] ([BusinessId], [UserNumber]);

GO

CREATE INDEX [IX_Conversations_AgentId] ON [dbo].[Conversations] ([AgentId]);

GO
