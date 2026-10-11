CREATE TABLE [sales].[RestaurantProductRoutes]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [StationId] UNIQUEIDENTIFIER NOT NULL,
    [ShortName] NVARCHAR(100) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantProductRoutes] PRIMARY KEY ([BusinessId],[ProductId]),
    CONSTRAINT [FK_RestaurantProductRoutes_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantProductRoutes_Product] FOREIGN KEY ([TenantId],[ProductId])
        REFERENCES [dbo].[Products] ([TenantId],[ProductId]),
    CONSTRAINT [FK_RestaurantProductRoutes_Station] FOREIGN KEY ([BusinessId],[StationId])
        REFERENCES [sales].[RestaurantStations] ([BusinessId],[StationId]),
    CONSTRAINT [CK_RestaurantProductRoutes_ShortName] CHECK ([ShortName] IS NULL OR LEN(LTRIM(RTRIM([ShortName])))>0)
);
GO
CREATE INDEX [IX_RestaurantProductRoutes_Station]
    ON [sales].[RestaurantProductRoutes] ([BusinessId],[StationId],[ProductId]);
GO
