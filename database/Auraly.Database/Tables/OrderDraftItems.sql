CREATE TABLE [dbo].[OrderDraftItems] (
    [OrderDraftItemId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [OrderDraftId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [ExternalProductId] NVARCHAR(300) NULL,
    [Sku] NVARCHAR(100) NULL,
    [ProductNameSnapshot] NVARCHAR(250) NOT NULL,
    [DescriptionSnapshot] NVARCHAR(MAX) NULL,
    [Quantity] DECIMAL(18, 2) NOT NULL,
    [UnitPrice] DECIMAL(18, 2) NOT NULL,
    [DiscountAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0,

    [LineTotal] DECIMAL(18, 2) NOT NULL,
    [RawPayloadJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_OrderDraftItems_OrderDrafts] FOREIGN KEY ([OrderDraftId])
        REFERENCES [dbo].[OrderDrafts] ([OrderDraftId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_OrderDraftItems_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderDraftItems_Products] FOREIGN KEY ([ProductId])
        REFERENCES [dbo].[Products] ([ProductId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderDraftItems_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_OrderDraftItems_OrderDraftId] ON [dbo].[OrderDraftItems] ([OrderDraftId]);
GO
CREATE INDEX [IX_OrderDraftItems_BusinessId] ON [dbo].[OrderDraftItems] ([BusinessId]);
GO
CREATE INDEX [IX_OrderDraftItems_ProductId] ON [dbo].[OrderDraftItems] ([ProductId]);
GO
CREATE INDEX [IX_OrderDraftItems_BusinessId_ExternalProductId] ON [dbo].[OrderDraftItems] ([BusinessId], [ExternalProductId]);
GO
