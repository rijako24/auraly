CREATE TABLE [dbo].[ProductCategories] (
    [ProductCategoryId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ParentProductCategoryId] UNIQUEIDENTIFIER NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [ExternalCategoryId] NVARCHAR(150) NULL,
    [Name] NVARCHAR(150) NOT NULL,
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [IsBrowsable] BIT NOT NULL DEFAULT 1,
    [LastSyncedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ProductCategories_Parent] FOREIGN KEY ([TenantId], [ParentProductCategoryId])
        REFERENCES [dbo].[ProductCategories] ([TenantId], [ProductCategoryId]),
    CONSTRAINT [FK_ProductCategories_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION,
    CONSTRAINT [UQ_ProductCategories_Tenant_Category] UNIQUE ([TenantId], [ProductCategoryId]),
    CONSTRAINT [FK_ProductCategories_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_ProductCategories_Parent]
    ON [dbo].[ProductCategories] ([TenantId], [ParentProductCategoryId], [DisplayOrder], [Name]);
GO

CREATE INDEX [IX_ProductCategories_TenantId]
    ON [dbo].[ProductCategories] ([TenantId]);
GO

CREATE UNIQUE INDEX [IX_ProductCategories_TenantId_Connection_ExternalCategoryId]
    ON [dbo].[ProductCategories] ([TenantId], [IntegrationConnectionId], [ExternalCategoryId])
    WHERE [IntegrationConnectionId] IS NOT NULL AND [ExternalCategoryId] IS NOT NULL;
GO

CREATE UNIQUE INDEX [IX_ProductCategories_TenantId_Connection_Name]
    ON [dbo].[ProductCategories] ([TenantId], [IntegrationConnectionId], [Name]);
GO

CREATE INDEX [IX_ProductCategories_Browse]
    ON [dbo].[ProductCategories] ([TenantId], [IsActive], [IsBrowsable], [DisplayOrder], [Name]);
GO
