CREATE TABLE [pricing].[PromotionBusinessScopes] (
    [PromotionId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_PromotionBusinessScopes] PRIMARY KEY ([PromotionId], [BusinessId]),
    CONSTRAINT [FK_PromotionBusinessScopes_Promotions] FOREIGN KEY ([PromotionId], [TenantId])
        REFERENCES [dbo].[Promotions] ([PromotionId], [TenantId]) ON DELETE CASCADE,
    CONSTRAINT [FK_PromotionBusinessScopes_Businesses] FOREIGN KEY ([BusinessId], [TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId], [TenantId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionBusinessScopes_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_PromotionBusinessScopes_BusinessId]
    ON [pricing].[PromotionBusinessScopes] ([BusinessId], [PromotionId]);
GO

CREATE INDEX [IX_PromotionBusinessScopes_TenantId]
    ON [pricing].[PromotionBusinessScopes] ([TenantId]);
GO
