-- Crea la tabla PaymentTransactions para auditoría e idempotencia del webhook Wompi.
-- Ejecutar con: sqlcmd -S .\LOCAL -d talkioai -U admin -P masterkey -i CreatePaymentTransactionsTable.sql

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PaymentTransactions')
BEGIN
    CREATE TABLE [dbo].[PaymentTransactions] (
        [PaymentTransactionId]  UNIQUEIDENTIFIER NOT NULL,
        [BusinessId]            UNIQUEIDENTIFIER NOT NULL,
        [ConversationId]        UNIQUEIDENTIFIER NOT NULL,
        [PaymentReferenceId]    NVARCHAR(200)   NOT NULL,
        [ProviderTransactionId] NVARCHAR(200)   NULL,
        [AmountInCents]         BIGINT          NOT NULL,
        [Currency]              NVARCHAR(10)    NOT NULL DEFAULT 'COP',
        [Status]                INT             NOT NULL DEFAULT 0,
        [CreatedAt]             DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        [ConfirmedAt]           DATETIME2       NULL,
        [WebhookPayloadJson]    NVARCHAR(MAX)   NULL,
        CONSTRAINT [PK_PaymentTransactions] PRIMARY KEY ([PaymentTransactionId]),
        CONSTRAINT [FK_PaymentTransactions_Businesses_BusinessId] 
            FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentTransactions_Conversations_ConversationId] 
            FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[Conversations]([ConversationId]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_PaymentTransactions_PaymentReferenceId] ON [dbo].[PaymentTransactions]([PaymentReferenceId]);
    CREATE INDEX [IX_PaymentTransactions_ConversationId] ON [dbo].[PaymentTransactions]([ConversationId]);
    CREATE INDEX [IX_PaymentTransactions_BusinessId] ON [dbo].[PaymentTransactions]([BusinessId]);
    PRINT 'Tabla PaymentTransactions creada correctamente.';
END
ELSE
    PRINT 'La tabla PaymentTransactions ya existe.';
