CREATE TABLE [dbo].[SalesDraftMutationReceipts] (
    [SalesDraftMutationReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SalesDraftId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(100) NOT NULL,
    [Operation] NVARCHAR(40) NOT NULL,
    [RequestHash] CHAR(64) NOT NULL,
    [ResultVersion] BIGINT NOT NULL,
    [CreatedAt] DATETIMEOFFSET NOT NULL,
    CONSTRAINT [PK_SalesDraftMutationReceipts] PRIMARY KEY ([SalesDraftMutationReceiptId]),
    CONSTRAINT [FK_SalesDraftMutationReceipts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesDraftMutationReceipts_SalesDrafts] FOREIGN KEY ([SalesDraftId])
        REFERENCES [dbo].[SalesDrafts] ([SalesDraftId])
);
GO

CREATE UNIQUE INDEX [UX_SalesDraftMutationReceipts_Business_Key]
    ON [dbo].[SalesDraftMutationReceipts] ([BusinessId], [IdempotencyKey]);
GO

CREATE INDEX [IX_SalesDraftMutationReceipts_Draft]
    ON [dbo].[SalesDraftMutationReceipts] ([SalesDraftId], [CreatedAt]);
GO
