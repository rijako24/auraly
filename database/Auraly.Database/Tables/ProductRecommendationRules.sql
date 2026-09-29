CREATE TABLE [dbo].[ProductRecommendationRules] (
    [ProductRecommendationRuleId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [MatchType] INT NOT NULL,
    [SourceProductId] UNIQUEIDENTIFIER NULL,
    [SourceValue] NVARCHAR(300) NULL,
    [RecommendedProductId] UNIQUEIDENTIFIER NULL,
    [RecommendedExternalProductId] NVARCHAR(300) NULL,
    [RecommendedSku] NVARCHAR(100) NULL,
    [RecommendedSearchText] NVARCHAR(300) NULL,
    [RecommendationType] INT NOT NULL DEFAULT 0,
    [Priority] INT NOT NULL DEFAULT 0,
    [Reason] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [StartsAtUtc] DATETIME2 NULL,
    [EndsAtUtc] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ProductRecommendationRules_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductRecommendationRules_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductRecommendationRules_SourceProducts] FOREIGN KEY ([TenantId], [SourceProductId])
        REFERENCES [dbo].[Products] ([TenantId], [ProductId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductRecommendationRules_RecommendedProducts] FOREIGN KEY ([TenantId], [RecommendedProductId])
        REFERENCES [dbo].[Products] ([TenantId], [ProductId]) ON DELETE NO ACTION,
    CONSTRAINT [CK_ProductRecommendationRules_MatchType] CHECK ([MatchType] IN (0, 1, 2, 3, 4)),
    CONSTRAINT [CK_ProductRecommendationRules_RecommendationType] CHECK ([RecommendationType] IN (0, 1, 2)),
    CONSTRAINT [CK_ProductRecommendationRules_Source] CHECK (
        [SourceProductId] IS NOT NULL OR NULLIF(LTRIM(RTRIM([SourceValue])), N'') IS NOT NULL),
    CONSTRAINT [CK_ProductRecommendationRules_Target] CHECK (
        [RecommendedProductId] IS NOT NULL
        OR NULLIF(LTRIM(RTRIM([RecommendedExternalProductId])), N'') IS NOT NULL
        OR NULLIF(LTRIM(RTRIM([RecommendedSku])), N'') IS NOT NULL),
    CONSTRAINT [CK_ProductRecommendationRules_Validity] CHECK (
        [StartsAtUtc] IS NULL OR [EndsAtUtc] IS NULL OR [StartsAtUtc] < [EndsAtUtc])
);

GO

CREATE INDEX [IX_ProductRecommendationRules_TenantId_Active]
    ON [dbo].[ProductRecommendationRules] ([TenantId], [IsActive], [Priority]);

GO

CREATE INDEX [IX_ProductRecommendationRules_Connection]
    ON [dbo].[ProductRecommendationRules] ([IntegrationConnectionId]);

GO

CREATE INDEX [IX_ProductRecommendationRules_SourceProduct]
    ON [dbo].[ProductRecommendationRules] ([SourceProductId]);

GO

CREATE INDEX [IX_ProductRecommendationRules_RecommendedProduct]
    ON [dbo].[ProductRecommendationRules] ([RecommendedProductId]);

GO
