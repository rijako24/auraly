CREATE TABLE [dbo].[InboundMessageReceipts] (
    [InboundMessageReceiptId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Provider] NVARCHAR(30) NOT NULL,
    [ProviderMessageId] NVARCHAR(128) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [ReceivedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ProcessingStartedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ProcessedAtUtc] DATETIME2 NULL,
    [UpdatedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
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
