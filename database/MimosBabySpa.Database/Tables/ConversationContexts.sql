CREATE TABLE [dbo].[ConversationContexts] (
    [ConversationContextId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [Field] NVARCHAR(100) NOT NULL,
    [Value] NVARCHAR(2000) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ConversationContexts_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE CASCADE
);

GO

CREATE INDEX [IX_ConversationContexts_ConversationId] ON [dbo].[ConversationContexts] ([ConversationId]);

GO

CREATE UNIQUE INDEX [IX_ConversationContexts_ConversationId_Field] ON [dbo].[ConversationContexts] ([ConversationId], [Field]);

GO
