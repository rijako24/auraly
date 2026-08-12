CREATE TABLE [dbo].[InboundMessageReceipts] (
    [InboundMessageReceiptId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Provider] NVARCHAR(30) NOT NULL,
    [ProviderMessageId] NVARCHAR(128) NOT NULL,
    [UserNumber] NVARCHAR(50) NOT NULL DEFAULT '',
    [CustomerName] NVARCHAR(200) NULL,
    [RawEntryJson] NVARCHAR(MAX) NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [ReceivedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [QueuedAtUtc] DATETIME2 NULL,
    [ProcessingDueAtUtc] DATETIME2 NULL,
    [ProcessingStartedAtUtc] DATETIME2 NOT NULL DEFAULT '0001-01-01T00:00:00',
    [ProcessedAtUtc] DATETIME2 NULL,
    [UpdatedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [AttemptCount] INT NOT NULL DEFAULT 0,
    [LastError] NVARCHAR(4000) NULL,
    CONSTRAINT [FK_InboundMessageReceipts_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [UQ_InboundMessageReceipts_ProviderMessage] UNIQUE ([BusinessId], [Provider], [ProviderMessageId])
);

GO

CREATE INDEX [IX_InboundMessageReceipts_Status_ProcessingStartedAtUtc]
    ON [dbo].[InboundMessageReceipts] ([Status], [ProcessingStartedAtUtc]);

GO

CREATE INDEX [IX_InboundMessageReceipts_ConversationPending]
    ON [dbo].[InboundMessageReceipts] ([BusinessId], [Provider], [UserNumber], [Status], [ReceivedAtUtc]);

GO
