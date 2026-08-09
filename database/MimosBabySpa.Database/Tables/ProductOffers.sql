CREATE TABLE [dbo].[ProductOffers] (
    [ProductOfferId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Condition] NVARCHAR(30) NOT NULL,
    [StorageGb] INT NULL,
    [Color] NVARCHAR(100) NULL,
    [VariantLabel] NVARCHAR(250) NULL,
    [UnitPrice] DECIMAL(18, 2) NOT NULL,
    [Currency] NVARCHAR(10) NOT NULL DEFAULT N'COP',
    [MinimumBatteryHealthPercent] INT NULL,
    [IsAvailable] BIT NOT NULL DEFAULT 1,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [PriceSourceUrl] NVARCHAR(1000) NULL,
    [PriceObservedAtUtc] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ProductOffers_Products] FOREIGN KEY ([ProductId])
        REFERENCES [dbo].[Products] ([ProductId]) ON DELETE CASCADE,
    CONSTRAINT [FK_ProductOffers_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_ProductOffers_Condition] CHECK ([Condition] IN (N'new', N'used', N'refurbished')),
    CONSTRAINT [CK_ProductOffers_Storage] CHECK ([StorageGb] IS NULL OR [StorageGb] > 0),
    CONSTRAINT [CK_ProductOffers_Price] CHECK ([UnitPrice] >= 0),
    CONSTRAINT [CK_ProductOffers_Battery] CHECK (
        [MinimumBatteryHealthPercent] IS NULL
        OR [MinimumBatteryHealthPercent] BETWEEN 1 AND 100)
);

GO

CREATE UNIQUE INDEX [UX_ProductOffers_Product_Condition_Storage_Color_Variant]
    ON [dbo].[ProductOffers] ([ProductId], [Condition], [StorageGb], [Color], [VariantLabel]);

GO

CREATE INDEX [IX_ProductOffers_Business_Active]
    ON [dbo].[ProductOffers] ([BusinessId], [Condition], [IsActive], [IsAvailable])
    INCLUDE ([ProductId], [StorageGb], [UnitPrice], [Currency]);
