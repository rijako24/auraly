CREATE TABLE [dbo].[Conversations] (
    [ConversationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [UserNumber] NVARCHAR(50) NOT NULL,
    [LastMessage] NVARCHAR(1000) NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CustomerName] NVARCHAR(100) NULL,
    [CustomerEmail] NVARCHAR(200) NULL,
    [State] INT NOT NULL DEFAULT 0,
    CONSTRAINT [FK_Conversations_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_Conversations_BusinessId_UserNumber] ON [dbo].[Conversations] ([BusinessId], [UserNumber]);

GO

CREATE INDEX [IX_Conversations_State] ON [dbo].[Conversations] ([State]);

GO
