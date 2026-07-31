CREATE TABLE [dbo].[Orders] (
    [OrderId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AgentId] UNIQUEIDENTIFIER NULL,
    [ConversationId] UNIQUEIDENTIFIER NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [CommerceWarehouseCode] NVARCHAR(100) NULL,
    [PaymentTransactionId] UNIQUEIDENTIFIER NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [Source] INT NOT NULL DEFAULT 0,
    [FulfillmentMode] INT NOT NULL DEFAULT 0,
    [Status] INT NOT NULL DEFAULT 0,
    [CustomerNameSnapshot] NVARCHAR(150) NULL,
    [CustomerEmailSnapshot] NVARCHAR(200) NULL,
    [CustomerPhoneSnapshot] NVARCHAR(50) NULL,
    [CustomerDocumentSnapshot] NVARCHAR(80) NULL,
    [DeliveryAddressSnapshot] NVARCHAR(500) NULL,
    [Notes] NVARCHAR(MAX) NULL,
    [Currency] NVARCHAR(10) NOT NULL DEFAULT N'COP',
    [Subtotal] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [DiscountTotal] DECIMAL(18, 2) NOT NULL DEFAULT 0,

    [Total] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [CustomerConfirmed] BIT NOT NULL DEFAULT 0,
    [ExternalOrderId] NVARCHAR(300) NULL,
    [ExternalDocumentNumber] NVARCHAR(300) NULL,
    [ExternalStatus] NVARCHAR(100) NULL,
    [IdempotencyKey] NVARCHAR(200) NULL,
    [CustomAttributesJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_Orders_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Orders_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE SET NULL,
    CONSTRAINT [FK_Orders_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE SET NULL,
    CONSTRAINT [FK_Orders_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Orders_PaymentTransactions] FOREIGN KEY ([PaymentTransactionId])
        REFERENCES [dbo].[PaymentTransactions] ([PaymentTransactionId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Orders_Customers] FOREIGN KEY ([CustomerId])
        REFERENCES [dbo].[Customers] ([CustomerId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_Orders_Source] CHECK ([Source] IN (0, 1, 2)),
    CONSTRAINT [CK_Orders_FulfillmentMode] CHECK ([FulfillmentMode] IN (0, 1)),
    CONSTRAINT [CK_Orders_Status] CHECK ([Status] IN (0, 1, 2, 3, 4, 5, 6, 7, 91))
);

GO

CREATE INDEX [IX_Orders_BusinessId] ON [dbo].[Orders] ([BusinessId]);
GO
CREATE INDEX [IX_Orders_ConversationId_Status] ON [dbo].[Orders] ([ConversationId], [Status]);
GO
CREATE INDEX [IX_Orders_BusinessId_CreatedAt] ON [dbo].[Orders] ([BusinessId], [CreatedAt]);
GO
CREATE INDEX [IX_Orders_BusinessId_Status] ON [dbo].[Orders] ([BusinessId], [Status]);
GO
CREATE INDEX [IX_Orders_BusinessId_CustomerId] ON [dbo].[Orders] ([BusinessId], [CustomerId])
    WHERE [CustomerId] IS NOT NULL;
GO
CREATE INDEX [IX_Orders_BusinessId_ExternalOrderId] ON [dbo].[Orders] ([BusinessId], [ExternalOrderId]);
GO
CREATE UNIQUE INDEX [UX_Orders_PaymentTransactionId]
    ON [dbo].[Orders] ([PaymentTransactionId])
    WHERE [PaymentTransactionId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_Orders_BusinessId_IdempotencyKey]
    ON [dbo].[Orders] ([BusinessId], [IdempotencyKey])
    WHERE [IdempotencyKey] IS NOT NULL;
GO

