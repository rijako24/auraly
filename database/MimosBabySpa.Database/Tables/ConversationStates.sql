CREATE TABLE [dbo].[ConversationStates] (
    [ConversationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Owner] TINYINT NOT NULL DEFAULT 0,
    [LastEscalatedAt] DATETIME2 NULL,
    [ConsecutiveDegradedTurns] INT NOT NULL DEFAULT 0,
    [LastUserMessage] NVARCHAR(MAX) NULL,
    [LastBotMessage] NVARCHAR(MAX) NULL,
    [VerificationsJson] NVARCHAR(MAX) NULL,
    [Version] INT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL,
    CONSTRAINT [FK_ConversationStates_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_ConversationStates_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_ConversationStates_ConversationId] ON [dbo].[ConversationStates] ([ConversationId]);

GO

CREATE INDEX [IX_ConversationStates_BusinessId] ON [dbo].[ConversationStates] ([BusinessId]);

GO
