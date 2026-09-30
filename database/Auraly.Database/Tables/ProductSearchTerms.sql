CREATE TABLE [dbo].[ProductSearchTerms] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Term] NVARCHAR(100) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_ProductSearchTerms] PRIMARY KEY ([TenantId], [ProductId], [Term]),
    CONSTRAINT [FK_ProductSearchTerms_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_ProductSearchTerms_Products] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId])
);
GO

CREATE INDEX [IX_ProductSearchTerms_Lookup]
    ON [dbo].[ProductSearchTerms] ([TenantId], [Term], [ProductId]);
GO
