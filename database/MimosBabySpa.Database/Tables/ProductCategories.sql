CREATE TABLE [dbo].[ProductCategories] (
    [ProductCategoryId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [ExternalCategoryId] NVARCHAR(150) NULL,
    [Name] NVARCHAR(150) NOT NULL,
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [IsBrowsable] BIT NOT NULL DEFAULT 1,
    [LastSyncedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ProductCategories_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductCategories_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_ProductCategories_BusinessId]
    ON [dbo].[ProductCategories] ([BusinessId]);
GO

CREATE UNIQUE INDEX [IX_ProductCategories_BusinessId_Connection_ExternalCategoryId]
    ON [dbo].[ProductCategories] ([BusinessId], [IntegrationConnectionId], [ExternalCategoryId])
    WHERE [IntegrationConnectionId] IS NOT NULL AND [ExternalCategoryId] IS NOT NULL;
GO

CREATE UNIQUE INDEX [IX_ProductCategories_BusinessId_Connection_Name]
    ON [dbo].[ProductCategories] ([BusinessId], [IntegrationConnectionId], [Name]);
GO

CREATE INDEX [IX_ProductCategories_Browse]
    ON [dbo].[ProductCategories] ([BusinessId], [IsActive], [IsBrowsable], [DisplayOrder], [Name]);
GO
