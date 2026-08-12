CREATE TABLE [dbo].[DocumentSeriesCursors]
(
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [NextConsecutive] BIGINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DocumentSeriesCursors] PRIMARY KEY CLUSTERED ([DocumentSeriesId]),
    CONSTRAINT [FK_DocumentSeriesCursors_DocumentSeries]
        FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [CK_DocumentSeriesCursors_Next] CHECK ([NextConsecutive] > 0)
);

GO

CREATE TABLE [dbo].[FiscalSeriesCursors]
(
    [SeriesId] UNIQUEIDENTIFIER NOT NULL,
    [NextConsecutive] BIGINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalSeriesCursors] PRIMARY KEY CLUSTERED ([SeriesId]),
    CONSTRAINT [FK_FiscalSeriesCursors_FiscalSeries]
        FOREIGN KEY ([SeriesId]) REFERENCES [dbo].[FiscalSeries] ([SeriesId]),
    CONSTRAINT [CK_FiscalSeriesCursors_Next] CHECK ([NextConsecutive] > 0)
);

GO

CREATE TABLE [dbo].[OnlineSalesCheckoutReceipts]
(
    [OnlineSalesCheckoutReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SalesDraftId] UNIQUEIDENTIFIER NOT NULL,
    [NextSalesDraftId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(100) NOT NULL,
    [RequestHash] CHAR(64) NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [PayloadJson] NVARCHAR(MAX) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_OnlineSalesCheckoutReceipts]
        PRIMARY KEY CLUSTERED ([OnlineSalesCheckoutReceiptId]),
    CONSTRAINT [FK_OnlineSalesCheckoutReceipts_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_OnlineSalesCheckoutReceipts_Drafts]
        FOREIGN KEY ([SalesDraftId]) REFERENCES [dbo].[SalesDrafts] ([SalesDraftId]),
    CONSTRAINT [FK_OnlineSalesCheckoutReceipts_NextDraft]
        FOREIGN KEY ([NextSalesDraftId]) REFERENCES [dbo].[SalesDrafts] ([SalesDraftId]),
    CONSTRAINT [UQ_OnlineSalesCheckoutReceipts_Draft] UNIQUE ([SalesDraftId]),
    CONSTRAINT [UQ_OnlineSalesCheckoutReceipts_Business_Key]
        UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [UQ_OnlineSalesCheckoutReceipts_Document] UNIQUE ([DocumentId]),
    CONSTRAINT [CK_OnlineSalesCheckoutReceipts_Status]
        CHECK ([Status] IN (N'Prepared', N'Completed', N'FiscalConflict'))
);

GO

CREATE INDEX [IX_OnlineSalesCheckoutReceipts_Business_Status]
    ON [dbo].[OnlineSalesCheckoutReceipts] ([BusinessId], [Status], [CreatedAt]);
