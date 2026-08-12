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
