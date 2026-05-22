CREATE TABLE [dbo].[PaymentTransactions] (
    [PaymentTransactionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [ReservationId] UNIQUEIDENTIFIER NULL,
    [PaymentReferenceId] NVARCHAR(200) NOT NULL,
    [ProviderTransactionId] NVARCHAR(200) NULL,
    [LinkUrl] NVARCHAR(1000) NULL,
    [AmountInCents] BIGINT NOT NULL,
    [Currency] NVARCHAR(10) NOT NULL,
    [Status] INT NOT NULL,
    [Source] INT NOT NULL DEFAULT 0,
    [ExpiresAt] DATETIME2 NULL,
    [WebhookPayloadJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ConfirmedAt] DATETIME2 NULL,
    [Snapshot_ServiceId] UNIQUEIDENTIFIER NULL,
    [Snapshot_ReservationDateTime] DATETIME2 NULL,
    [Snapshot_PreferredEmployeeId] UNIQUEIDENTIFIER NULL,
    [Snapshot_DurationMinutes] INT NULL,
    [Snapshot_CustomerName] NVARCHAR(200) NULL,
    [Snapshot_CustomerEmail] NVARCHAR(200) NULL,
    [Snapshot_CustomerPhone] NVARCHAR(50) NULL,
    [Snapshot_AddOnIds] NVARCHAR(500) NULL,
    [Snapshot_CustomAttributesJson] NVARCHAR(MAX) NULL,
    [RequiresRescheduling] BIT NOT NULL DEFAULT 0,
    [RequiresRefund] BIT NOT NULL DEFAULT 0,
    CONSTRAINT [FK_PaymentTransactions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentTransactions_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentTransactions_Reservations] FOREIGN KEY ([ReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentTransactions_SnapshotService] FOREIGN KEY ([Snapshot_ServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_PaymentTransactions_PaymentReferenceId] ON [dbo].[PaymentTransactions] ([PaymentReferenceId]);

GO

CREATE INDEX [IX_PaymentTransactions_BusinessId] ON [dbo].[PaymentTransactions] ([BusinessId]);

GO

CREATE INDEX [IX_PaymentTransactions_ConversationId] ON [dbo].[PaymentTransactions] ([ConversationId]);

GO

CREATE INDEX [IX_PaymentTransactions_ReservationId] ON [dbo].[PaymentTransactions] ([ReservationId]);

GO

CREATE INDEX [IX_PaymentTransactions_Snapshot_Slot] ON [dbo].[PaymentTransactions] ([Snapshot_ServiceId], [Snapshot_ReservationDateTime])
    WHERE [Snapshot_ServiceId] IS NOT NULL;

GO
