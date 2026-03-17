CREATE TABLE [dbo].[PaymentTransactions] (
    [PaymentTransactionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentReferenceId] NVARCHAR(200) NOT NULL,
    [ProviderTransactionId] NVARCHAR(200) NULL,
    [AmountInCents] BIGINT NOT NULL,
    [Currency] NVARCHAR(10) NOT NULL,
    [Status] INT NOT NULL,
    [Source] INT NOT NULL DEFAULT 0,
    [WebhookPayloadJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ConfirmedAt] DATETIME2 NULL,
    CONSTRAINT [FK_PaymentTransactions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentTransactions_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_PaymentTransactions_PaymentReferenceId] ON [dbo].[PaymentTransactions] ([PaymentReferenceId]);

GO

CREATE INDEX [IX_PaymentTransactions_BusinessId] ON [dbo].[PaymentTransactions] ([BusinessId]);

GO

CREATE INDEX [IX_PaymentTransactions_ConversationId] ON [dbo].[PaymentTransactions] ([ConversationId]);

GO
