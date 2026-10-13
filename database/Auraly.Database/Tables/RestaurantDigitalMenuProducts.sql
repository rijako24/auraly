CREATE TABLE [sales].[RestaurantDigitalMenuProducts]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [PublicDescription] NVARCHAR(400) NULL,
    [SortOrder] INT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_RestaurantDigitalMenuProducts] PRIMARY KEY ([BusinessId],[ProductId]),
    CONSTRAINT [FK_RestaurantDigitalMenuProducts_Menu] FOREIGN KEY ([BusinessId])
        REFERENCES [sales].[RestaurantDigitalMenus] ([BusinessId]),
    CONSTRAINT [FK_RestaurantDigitalMenuProducts_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantDigitalMenuProducts_Product] FOREIGN KEY ([TenantId],[ProductId])
        REFERENCES [dbo].[Products] ([TenantId],[ProductId]),
    CONSTRAINT [CK_RestaurantDigitalMenuProducts_Description]
        CHECK ([PublicDescription] IS NULL OR LEN(LTRIM(RTRIM([PublicDescription])))>0)
);
GO
CREATE INDEX [IX_RestaurantDigitalMenuProducts_Order]
    ON [sales].[RestaurantDigitalMenuProducts] ([BusinessId],[SortOrder],[ProductId]);
GO
