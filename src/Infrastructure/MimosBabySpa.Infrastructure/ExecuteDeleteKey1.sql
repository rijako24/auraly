BEGIN TRANSACTION;
GO

DROP TABLE [ConversationContexts];
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260120011611_RemoveConversationContext', N'8.0.0');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [ConversationContexts] (
    [ConversationContextId] uniqueidentifier NOT NULL,
    [ConversationId] uniqueidentifier NOT NULL,
    [Field] nvarchar(100) NOT NULL,
    [Value] nvarchar(500) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_ConversationContexts] PRIMARY KEY ([ConversationContextId]),
    CONSTRAINT [FK_ConversationContexts_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([ConversationId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_ConversationContexts_ConversationId] ON [ConversationContexts] ([ConversationId]);
GO

CREATE INDEX [IX_ConversationContexts_ConversationId_Field] ON [ConversationContexts] ([ConversationId], [Field]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260120012830_RecreateConversationContext', N'8.0.0');
GO

COMMIT;
GO

