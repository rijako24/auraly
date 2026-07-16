CREATE TABLE [dbo].[ProductAliases] (
    [ProductAliasId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] INT NOT NULL DEFAULT 0,
    [CustomerKey] NVARCHAR(100) NOT NULL DEFAULT N'',
    [Alias] NVARCHAR(250) NOT NULL,
    [NormalizedAlias] NVARCHAR(250) NOT NULL,
    [Kind] INT NOT NULL DEFAULT 0,
    [ResolutionMode] INT NOT NULL DEFAULT 0,
    [Source] INT NOT NULL DEFAULT 0,
    [Status] INT NOT NULL DEFAULT 0,
    [UsageCount] INT NOT NULL DEFAULT 0,
    [LastConfirmedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductAliases_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_ProductAliases_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]) ON DELETE CASCADE,
    CONSTRAINT [CK_ProductAliases_Scope] CHECK ([Scope] IN (0, 1)),
    CONSTRAINT [CK_ProductAliases_Kind] CHECK ([Kind] IN (0, 1, 2)),
    CONSTRAINT [CK_ProductAliases_ResolutionMode] CHECK ([ResolutionMode] IN (0, 1)),
    CONSTRAINT [CK_ProductAliases_Source] CHECK ([Source] IN (0, 1, 2)),
    CONSTRAINT [CK_ProductAliases_Status] CHECK ([Status] IN (0, 1, 2)),
    CONSTRAINT [CK_ProductAliases_CustomerScope] CHECK (([Scope] = 0 AND [CustomerKey] = N'') OR ([Scope] = 1 AND LEN([CustomerKey]) > 0))
);
GO

CREATE UNIQUE INDEX [UX_ProductAliases_ProductMapping]
    ON [dbo].[ProductAliases] ([BusinessId], [ProductId], [Scope], [CustomerKey], [NormalizedAlias]);
GO
CREATE UNIQUE INDEX [UX_ProductAliases_AutoResolve]
    ON [dbo].[ProductAliases] ([BusinessId], [Scope], [CustomerKey], [NormalizedAlias])
    WHERE [Status] = 1 AND [ResolutionMode] = 1;
GO

CREATE INDEX [IX_ProductAliases_Resolve]
    ON [dbo].[ProductAliases] ([BusinessId], [NormalizedAlias], [Scope], [CustomerKey], [Status])
    INCLUDE ([ProductId], [ResolutionMode], [Alias], [UsageCount]);
GO
