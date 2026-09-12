CREATE TABLE [dbo].[PricePublicationAudits]
(
    [PricePublicationAuditId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ProductPriceId] UNIQUEIDENTIFIER NOT NULL,
    [ProposalId] UNIQUEIDENTIFIER NULL,
    [PublicationOrigin] NVARCHAR(24) NOT NULL,
    [PreviousSalePrice] DECIMAL(19,4) NULL,
    [PublishedSalePrice] DECIMAL(19,4) NOT NULL,
    [CostBasisAmount] DECIMAL(19,6) NULL,
    [EffectiveMarginPercent] DECIMAL(9,6) NULL,
    [InputMode] NVARCHAR(16) NOT NULL,
    [PublishedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [PublishedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PricePublicationAudits] PRIMARY KEY CLUSTERED ([PricePublicationAuditId]),
    CONSTRAINT [FK_PricePublicationAudits_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_PricePublicationAudits_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_PricePublicationAudits_ProductPrices] FOREIGN KEY ([ProductPriceId]) REFERENCES [dbo].[ProductPrices] ([ProductPriceId]),
    CONSTRAINT [FK_PricePublicationAudits_Proposals] FOREIGN KEY ([ProposalId]) REFERENCES [dbo].[PriceRevisionProposals] ([PriceRevisionProposalId]),
    CONSTRAINT [CK_PricePublicationAudits_Origin] CHECK ([PublicationOrigin] IN (N'ReceiptProposal',N'Manual',N'LinkedProduct')),
    CONSTRAINT [CK_PricePublicationAudits_Values] CHECK ([PreviousSalePrice] IS NULL OR [PreviousSalePrice] >= 0),
    CONSTRAINT [CK_PricePublicationAudits_Published] CHECK ([PublishedSalePrice] >= 0 AND ([CostBasisAmount] IS NULL OR [CostBasisAmount] >= 0)),
    CONSTRAINT [CK_PricePublicationAudits_InputMode] CHECK ([InputMode] IN (N'Margin',N'SalePrice'))
);
GO
CREATE INDEX [IX_PricePublicationAudits_Product]
    ON [dbo].[PricePublicationAudits] ([BusinessId],[ProductId],[PublishedAt] DESC);
GO

CREATE TABLE [dbo].[ProductPricePreparations]
(
    [ProductPricePreparationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [SourceProposalId] UNIQUEIDENTIFIER NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NULL,
    [SourceLineNumber] INT NULL,
    [SourceProductId] UNIQUEIDENTIFIER NULL,
    [PreparationOrigin] NVARCHAR(24) NOT NULL,
    [PublicAmountSnapshot] DECIMAL(19,4) NOT NULL,
    [PreparedAmount] DECIMAL(19,4) NOT NULL,
    [CostBasisType] NVARCHAR(32) NULL,
    [CostBasisAmount] DECIMAL(19,6) NULL,
    [TargetMarginPercent] DECIMAL(9,6) NULL,
    [EffectiveMarginPercent] DECIMAL(9,6) NULL,
    [InputMode] NVARCHAR(16) NOT NULL,
    [RoundingIncrement] DECIMAL(19,4) NOT NULL,
    [RoundingMode] NVARCHAR(16) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [PreparedByUserId] UNIQUEIDENTIFIER NULL,
    [PreparedAt] DATETIMEOFFSET(7) NOT NULL,
    [SupersededAt] DATETIMEOFFSET(7) NULL,
    [PublishedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_ProductPricePreparations] PRIMARY KEY CLUSTERED ([ProductPricePreparationId]),
    CONSTRAINT [FK_ProductPricePreparations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_ProductPricePreparations_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_ProductPricePreparations_Proposals] FOREIGN KEY ([SourceProposalId]) REFERENCES [dbo].[PriceRevisionProposals] ([PriceRevisionProposalId]),
    CONSTRAINT [FK_ProductPricePreparations_SourceProducts] FOREIGN KEY ([SourceProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_ProductPricePreparations_Users] FOREIGN KEY ([PreparedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_ProductPricePreparations_Origin] CHECK ([PreparationOrigin] IN (N'GoodsReceipt',N'ProposalReview',N'Product',N'LinkedProduct',N'Migration')),
    CONSTRAINT [CK_ProductPricePreparations_Amounts] CHECK ([PublicAmountSnapshot] >= 0 AND [PreparedAmount] >= 0 AND ([CostBasisAmount] IS NULL OR [CostBasisAmount] >= 0)),
    CONSTRAINT [CK_ProductPricePreparations_Margin] CHECK (([TargetMarginPercent] IS NULL OR [TargetMarginPercent] BETWEEN 0 AND 99.999999) AND ([EffectiveMarginPercent] IS NULL OR [EffectiveMarginPercent] < 100)),
    CONSTRAINT [CK_ProductPricePreparations_InputMode] CHECK ([InputMode] IN (N'Margin',N'SalePrice')),
    CONSTRAINT [CK_ProductPricePreparations_Rounding] CHECK ([RoundingIncrement] > 0 AND [RoundingMode] IN (N'Nearest',N'Up',N'Down')),
    CONSTRAINT [CK_ProductPricePreparations_Status] CHECK ([Status] IN (N'Pending',N'Published',N'Superseded',N'Discarded')),
    CONSTRAINT [CK_ProductPricePreparations_Lifecycle] CHECK (
        ([Status]=N'Pending' AND [SupersededAt] IS NULL AND [PublishedAt] IS NULL)
        OR ([Status]=N'Published' AND [PublishedAt] IS NOT NULL)
        OR ([Status] IN (N'Superseded',N'Discarded') AND [SupersededAt] IS NOT NULL))
);
GO
CREATE UNIQUE INDEX [UX_ProductPricePreparations_Pending]
    ON [dbo].[ProductPricePreparations] ([BusinessId],[ProductId])
    WHERE [Status]=N'Pending';
GO
CREATE INDEX [IX_ProductPricePreparations_History]
    ON [dbo].[ProductPricePreparations] ([BusinessId],[ProductId],[PreparedAt] DESC)
    INCLUDE ([Status],[PreparationOrigin],[PreparedAmount],[CostBasisAmount],[EffectiveMarginPercent]);
GO
