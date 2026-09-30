CREATE TABLE [dbo].[ProductImages] (
    [ProductImageId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductOfferId] UNIQUEIDENTIFIER NULL,
    [MediaUrl] NVARCHAR(1500) NOT NULL,
    [AltText] NVARCHAR(300) NULL,
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    [IsPrimary] BIT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ProductImages_Products] FOREIGN KEY ([TenantId], [ProductId])
        REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [FK_ProductImages_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_ProductImages_ProductOffers] FOREIGN KEY ([TenantId], [ProductOfferId])
        REFERENCES [dbo].[ProductOffers] ([TenantId], [ProductOfferId]),
    CONSTRAINT [CK_ProductImages_MediaUrl] CHECK (LEN(LTRIM(RTRIM([MediaUrl]))) > 0)
);

GO

CREATE INDEX [IX_ProductImages_Product_Offer_Order]
    ON [dbo].[ProductImages] ([ProductId], [ProductOfferId], [IsActive], [DisplayOrder]);

GO

CREATE UNIQUE INDEX [UX_ProductImages_PrimaryOffer]
    ON [dbo].[ProductImages] ([ProductOfferId])
    WHERE [ProductOfferId] IS NOT NULL AND [IsPrimary] = 1 AND [IsActive] = 1;
