CREATE TABLE [dbo].[CatalogSyncSessionProducts] (
    [CatalogSyncSessionId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_CatalogSyncSessionProducts] PRIMARY KEY ([CatalogSyncSessionId], [ProductId]),
    CONSTRAINT [FK_CatalogSyncSessionProducts_Sessions]
        FOREIGN KEY ([CatalogSyncSessionId])
        REFERENCES [dbo].[CatalogSyncSessions] ([CatalogSyncSessionId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_CatalogSyncSessionProducts_Products]
        FOREIGN KEY ([ProductId])
        REFERENCES [dbo].[Products] ([ProductId])
);
GO
