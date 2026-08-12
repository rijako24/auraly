CREATE TABLE [dbo].[ProductImages] (
    [ProductImageId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductOfferId] UNIQUEIDENTIFIER NULL,
    [MediaUrl] NVARCHAR(1500) NOT NULL,
    [AltText] NVARCHAR(300) NULL,
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    [IsPrimary] BIT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ProductImages_Products] FOREIGN KEY ([ProductId])
        REFERENCES [dbo].[Products] ([ProductId]) ON DELETE CASCADE,
    CONSTRAINT [FK_ProductImages_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_ProductImages_ProductOffers] FOREIGN KEY ([ProductOfferId])
        REFERENCES [dbo].[ProductOffers] ([ProductOfferId]),
    CONSTRAINT [CK_ProductImages_MediaUrl] CHECK (LEN(LTRIM(RTRIM([MediaUrl]))) > 0)
);

GO

CREATE INDEX [IX_ProductImages_Product_Offer_Order]
    ON [dbo].[ProductImages] ([ProductId], [ProductOfferId], [IsActive], [DisplayOrder]);

GO

CREATE UNIQUE INDEX [UX_ProductImages_PrimaryOffer]
    ON [dbo].[ProductImages] ([ProductOfferId])
    WHERE [ProductOfferId] IS NOT NULL AND [IsPrimary] = 1 AND [IsActive] = 1;
