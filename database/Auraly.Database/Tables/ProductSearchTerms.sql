CREATE TABLE [dbo].[ProductSearchTerms] (
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Term] NVARCHAR(100) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_ProductSearchTerms] PRIMARY KEY ([BusinessId], [ProductId], [Term]),
    CONSTRAINT [FK_ProductSearchTerms_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_ProductSearchTerms_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_ProductSearchTerms_Lookup]
    ON [dbo].[ProductSearchTerms] ([BusinessId], [Term], [ProductId]);
GO
