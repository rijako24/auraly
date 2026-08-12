CREATE TABLE [dbo].[OrderItems] (
    [OrderItemId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [ExternalProductId] NVARCHAR(300) NULL,
    [Sku] NVARCHAR(100) NULL,
    [ProductCodeSnapshot] NVARCHAR(64) NULL,
    [ProductNameSnapshot] NVARCHAR(250) NOT NULL,
    [DescriptionSnapshot] NVARCHAR(MAX) NULL,
    [UnitCodeSnapshot] NVARCHAR(24) NULL,
    [Quantity] DECIMAL(18, 2) NOT NULL,
    [UnitPrice] DECIMAL(18, 2) NOT NULL,
    [DiscountAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [TaxAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0,

    [LineTotal] DECIMAL(18, 2) NOT NULL,
    [RawPayloadJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_OrderItems_Orders] FOREIGN KEY ([OrderId])
        REFERENCES [dbo].[Orders] ([OrderId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_OrderItems_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderItems_Products] FOREIGN KEY ([ProductId])
        REFERENCES [dbo].[Products] ([ProductId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderItems_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_OrderItems_OrderId] ON [dbo].[OrderItems] ([OrderId]);
GO
CREATE INDEX [IX_OrderItems_BusinessId] ON [dbo].[OrderItems] ([BusinessId]);
GO
CREATE INDEX [IX_OrderItems_ProductId] ON [dbo].[OrderItems] ([ProductId]);
GO
CREATE INDEX [IX_OrderItems_BusinessId_ExternalProductId] ON [dbo].[OrderItems] ([BusinessId], [ExternalProductId]);
GO
