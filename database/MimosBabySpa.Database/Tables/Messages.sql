CREATE TABLE [dbo].[Messages] (
    [MessageId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [Sender] NVARCHAR(20) NOT NULL,
    [MessageText] NVARCHAR(2000) NOT NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_Messages_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE CASCADE
);

GO

CREATE INDEX [IX_Messages_ConversationId] ON [dbo].[Messages] ([ConversationId]);

GO
