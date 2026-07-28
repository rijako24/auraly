CREATE TABLE [dbo].[OrderDrafts] (
    [OrderDraftId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AgentId] UNIQUEIDENTIFIER NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [CommerceWarehouseCode] NVARCHAR(100) NULL,
    [PaymentTransactionId] UNIQUEIDENTIFIER NULL,
    [Source] INT NOT NULL DEFAULT 0,
    [FulfillmentMode] INT NOT NULL DEFAULT 0,
    [CustomerNameSnapshot] NVARCHAR(150) NULL,
    [CustomerEmailSnapshot] NVARCHAR(200) NULL,
    [CustomerPhoneSnapshot] NVARCHAR(50) NULL,
    [CustomerDocumentSnapshot] NVARCHAR(80) NULL,
    [DeliveryAddressSnapshot] NVARCHAR(500) NULL,
    [Notes] NVARCHAR(MAX) NULL,
    [Currency] NVARCHAR(10) NOT NULL DEFAULT N'COP',
    [Subtotal] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [DiscountTotal] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [TaxTotal] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [Total] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [CustomerConfirmed] BIT NOT NULL DEFAULT 0,
    [CustomAttributesJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_OrderDrafts_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderDrafts_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE SET NULL,
    CONSTRAINT [FK_OrderDrafts_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_OrderDrafts_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderDrafts_PaymentTransactions] FOREIGN KEY ([PaymentTransactionId])
        REFERENCES [dbo].[PaymentTransactions] ([PaymentTransactionId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_OrderDrafts_Source] CHECK ([Source] IN (0, 1, 2)),
    CONSTRAINT [CK_OrderDrafts_FulfillmentMode] CHECK ([FulfillmentMode] IN (0, 1))
);

GO

CREATE INDEX [IX_OrderDrafts_BusinessId] ON [dbo].[OrderDrafts] ([BusinessId]);
GO
CREATE UNIQUE INDEX [UX_OrderDrafts_BusinessId_ConversationId] ON [dbo].[OrderDrafts] ([BusinessId], [ConversationId]);
GO
CREATE UNIQUE INDEX [UX_OrderDrafts_PaymentTransactionId]
    ON [dbo].[OrderDrafts] ([PaymentTransactionId])
    WHERE [PaymentTransactionId] IS NOT NULL;
GO