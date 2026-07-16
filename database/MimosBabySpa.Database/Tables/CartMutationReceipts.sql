CREATE TABLE [dbo].[CartMutationReceipts] (
    [CartMutationReceiptId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(200) NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_CartMutationReceipts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CartMutationReceipts_Conversations] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[Conversations] ([ConversationId])
);
GO

CREATE UNIQUE INDEX [UX_CartMutationReceipts_Idempotency]
    ON [dbo].[CartMutationReceipts] ([BusinessId], [ConversationId], [IdempotencyKey]);
GO
CREATE INDEX [IX_CartMutationReceipts_CreatedAt]
    ON [dbo].[CartMutationReceipts] ([CreatedAt]);
GO
