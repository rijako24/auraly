CREATE TABLE [dbo].[Promotions] (
    [PromotionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(1000) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [StartsAtUtc] DATETIME2 NULL,
    [EndsAtUtc] DATETIME2 NULL,
    [Priority] INT NOT NULL DEFAULT 0,
    [IsCombinable] BIT NOT NULL DEFAULT 0,
    [AppliesToAllBusinesses] BIT NOT NULL DEFAULT 0,
    [CouponCode] NVARCHAR(80) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Promotions_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION,
    CONSTRAINT [UQ_Promotions_Promotion_Tenant] UNIQUE ([PromotionId], [TenantId])
);

GO

CREATE INDEX [IX_Promotions_TenantId_Active_Window]
    ON [dbo].[Promotions] ([TenantId], [IsActive], [StartsAtUtc], [EndsAtUtc]);
GO
CREATE INDEX [IX_Promotions_TenantId_CouponCode]
    ON [dbo].[Promotions] ([TenantId], [CouponCode])
    WHERE [CouponCode] IS NOT NULL;
GO
