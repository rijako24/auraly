CREATE TABLE [dbo].[Tenants] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantKey] NVARCHAR(64) NOT NULL
        CONSTRAINT [DF_Tenants_TenantKey] DEFAULT (CONCAT(N'@tenant-',LOWER(REPLACE(CONVERT(NVARCHAR(36),NEWID()),N'-',N'')))),
    [Name] NVARCHAR(200) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [MaximumUsers] INT NOT NULL CONSTRAINT [DF_Tenants_MaximumUsers] DEFAULT (5),
    [MaximumEnrolledDevices] INT NOT NULL CONSTRAINT [DF_Tenants_MaximumEnrolledDevices] DEFAULT (1),
    [InventoryCostBasis] NVARCHAR(32) NOT NULL CONSTRAINT [DF_Tenants_InventoryCostBasis] DEFAULT N'LatestReceiptCost',
    [AllowPromotionChannelCombination] BIT NOT NULL CONSTRAINT [DF_Tenants_AllowPromotionChannelCombination] DEFAULT (0),
    CONSTRAINT [CK_Tenants_MaximumUsers] CHECK ([MaximumUsers] >= 1),
    CONSTRAINT [CK_Tenants_MaximumEnrolledDevices] CHECK ([MaximumEnrolledDevices] >= 0),
    CONSTRAINT [CK_Tenants_InventoryCostBasis] CHECK ([InventoryCostBasis] IN (N'LatestReceiptCost',N'WeightedAverageCost')),
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL
);

GO
CREATE UNIQUE INDEX [UX_Tenants_TenantKey] ON [dbo].[Tenants] ([TenantKey]);

GO

ALTER TABLE [dbo].[Tenants] ADD CONSTRAINT [CK_Tenants_TenantKey]
    CHECK ([TenantKey] LIKE N'@%' AND LEN([TenantKey]) BETWEEN 3 AND 64);

GO

CREATE UNIQUE INDEX [IX_Tenants_Email] ON [dbo].[Tenants] ([Email]);

GO
