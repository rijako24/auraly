CREATE TABLE [dbo].[PaymentTransactions] (
    [PaymentTransactionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NULL,
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
    [CheckoutKind] INT NOT NULL DEFAULT 0,
    [CheckoutSnapshotJson] NVARCHAR(MAX) NULL,
    [MerchantConfigurationVersion] INT NOT NULL CONSTRAINT [DF_PaymentTransactions_MerchantConfigurationVersion] DEFAULT (1),
    [QuoteHash] NVARCHAR(128) NULL,
    [ConfirmationOutcome] NVARCHAR(100) NULL,
    [RequiresRescheduling] BIT NOT NULL DEFAULT 0,
    [RequiresRefund] BIT NOT NULL DEFAULT 0,
    [SupersededAt] DATETIME2 NULL,
    [SupersededByPaymentTransactionId] UNIQUEIDENTIFIER NULL,
    [SubjectType] NVARCHAR(40) NULL,
    [SubjectId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [FK_PaymentTransactions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentTransactions_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentTransactions_Reservations] FOREIGN KEY ([ReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentTransactions_SupersededBy] FOREIGN KEY ([SupersededByPaymentTransactionId])
        REFERENCES [dbo].[PaymentTransactions] ([PaymentTransactionId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_PaymentTransactions_Subject] CHECK (
        ([SubjectType] IS NULL AND [SubjectId] IS NULL)
        OR ([SubjectType] IN (N'Reservation',N'Enrollment',N'Order',N'TenantProvisioning',N'TenantSubscription') AND [SubjectId] IS NOT NULL)),
    CONSTRAINT [CK_PaymentTransactions_MerchantConfigurationVersion] CHECK ([MerchantConfigurationVersion] > 0)
);

GO

CREATE UNIQUE INDEX [IX_PaymentTransactions_PaymentReferenceId] ON [dbo].[PaymentTransactions] ([PaymentReferenceId]);

GO

CREATE UNIQUE INDEX [IX_PaymentTransactions_ManualProviderReference]
    ON [dbo].[PaymentTransactions] ([ProviderTransactionId])
    WHERE [Source] = 1 AND [ProviderTransactionId] IS NOT NULL;

GO

CREATE UNIQUE INDEX [IX_PaymentTransactions_AutomatedMerchantTransaction]
    ON [dbo].[PaymentTransactions] ([BusinessId],[MerchantConfigurationVersion],[ProviderTransactionId])
    WHERE [Source] = 0 AND [ProviderTransactionId] IS NOT NULL;

GO

CREATE INDEX [IX_PaymentTransactions_BusinessId] ON [dbo].[PaymentTransactions] ([BusinessId]);

GO

CREATE INDEX [IX_PaymentTransactions_ConversationId] ON [dbo].[PaymentTransactions] ([ConversationId]);

GO

CREATE INDEX [IX_PaymentTransactions_ReservationId] ON [dbo].[PaymentTransactions] ([ReservationId]);

GO

CREATE INDEX [IX_PaymentTransactions_CheckoutKind] ON [dbo].[PaymentTransactions] ([CheckoutKind]);

GO

CREATE INDEX [IX_PaymentTransactions_Subject_Status] ON [dbo].[PaymentTransactions] ([SubjectType],[SubjectId],[Status])
    WHERE [SubjectType] IS NOT NULL AND [SubjectId] IS NOT NULL;

GO
