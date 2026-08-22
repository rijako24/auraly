CREATE TABLE [dbo].[SalesDraftLines] (
    [SalesDraftLineId] UNIQUEIDENTIFIER NOT NULL,
    [SalesDraftId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCode] NVARCHAR(64) NOT NULL,
    [Description] NVARCHAR(250) NOT NULL,
    [UnitCode] NVARCHAR(24) NOT NULL,
    [TaxCode] NVARCHAR(16) NOT NULL,
    [TaxRate] DECIMAL(9, 4) NOT NULL,
    [Quantity] DECIMAL(18, 4) NOT NULL,
    [BaseUnitPrice] DECIMAL(18, 2) NOT NULL,
    [UnitPrice] DECIMAL(18, 2) NOT NULL,
    [CurrencyCode] NVARCHAR(3) NOT NULL,
    [PriceSource] NVARCHAR(24) NOT NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NULL,
    [DiscountAmount] DECIMAL(18, 2) NOT NULL CONSTRAINT [DF_SalesDraftLines_Discount] DEFAULT 0,
    [Note] NVARCHAR(300) NULL,
    [Position] INT NOT NULL,
    CONSTRAINT [PK_SalesDraftLines] PRIMARY KEY ([SalesDraftLineId]),
    CONSTRAINT [FK_SalesDraftLines_SalesDrafts] FOREIGN KEY ([SalesDraftId])
        REFERENCES [dbo].[SalesDrafts] ([SalesDraftId]) ON DELETE CASCADE,
    CONSTRAINT [FK_SalesDraftLines_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_SalesDraftLines_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_SalesDraftLines_Amounts]
        CHECK ([BaseUnitPrice] >= 0 AND [UnitPrice] >= 0 AND [DiscountAmount] >= 0),
    CONSTRAINT [CK_SalesDraftLines_TaxRate] CHECK ([TaxRate] >= 0)
);
GO

CREATE INDEX [IX_SalesDraftLines_Draft_Product]
    ON [dbo].[SalesDraftLines] ([SalesDraftId], [ProductId]);
GO

CREATE UNIQUE INDEX [UX_SalesDraftLines_Draft_Position]
    ON [dbo].[SalesDraftLines] ([SalesDraftId], [Position]);
GO
