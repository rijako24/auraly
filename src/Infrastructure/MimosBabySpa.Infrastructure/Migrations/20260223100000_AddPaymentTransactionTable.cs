using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Crea la tabla PaymentTransactions para auditoría e idempotencia del webhook.
    /// </summary>
    [Migration("20260223100000_AddPaymentTransactionTable")]
    public partial class AddPaymentTransactionTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PaymentTransactions')
                BEGIN
                    CREATE TABLE [dbo].[PaymentTransactions] (
                        [PaymentTransactionId] UNIQUEIDENTIFIER NOT NULL,
                        [BusinessId]           UNIQUEIDENTIFIER NOT NULL,
                        [ConversationId]      UNIQUEIDENTIFIER NOT NULL,
                        [PaymentReferenceId]  NVARCHAR(200)    NOT NULL,
                        [ProviderTransactionId] NVARCHAR(200)   NULL,
                        [AmountInCents]       BIGINT           NOT NULL,
                        [Currency]            NVARCHAR(10)     NOT NULL DEFAULT 'COP',
                        [Status]              INT              NOT NULL DEFAULT 0,
                        [CreatedAt]           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
                        [ConfirmedAt]         DATETIME2        NULL,
                        [WebhookPayloadJson]  NVARCHAR(MAX)    NULL,
                        CONSTRAINT [PK_PaymentTransactions] PRIMARY KEY ([PaymentTransactionId]),
                        CONSTRAINT [FK_PaymentTransactions_Businesses_BusinessId] 
                            FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_PaymentTransactions_Conversations_ConversationId] 
                            FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[Conversations]([ConversationId]) ON DELETE NO ACTION
                    );
                    CREATE UNIQUE INDEX [IX_PaymentTransactions_PaymentReferenceId] ON [dbo].[PaymentTransactions]([PaymentReferenceId]);
                    CREATE INDEX [IX_PaymentTransactions_ConversationId] ON [dbo].[PaymentTransactions]([ConversationId]);
                    CREATE INDEX [IX_PaymentTransactions_BusinessId] ON [dbo].[PaymentTransactions]([BusinessId]);
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PaymentTransactions')
                    DROP TABLE [dbo].[PaymentTransactions];
            ");
        }
    }
}
