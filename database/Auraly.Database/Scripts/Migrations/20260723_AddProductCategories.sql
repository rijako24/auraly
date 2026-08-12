SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[ProductCategories]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProductCategories] (
        [ProductCategoryId] UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT [DF_ProductCategories_ProductCategoryId] DEFAULT NEWID(),
        [BusinessId] UNIQUEIDENTIFIER NOT NULL,
        [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
        [ExternalCategoryId] NVARCHAR(150) NULL,
        [Name] NVARCHAR(150) NOT NULL,
        [DisplayOrder] INT NOT NULL
            CONSTRAINT [DF_ProductCategories_DisplayOrder] DEFAULT 0,
        [IsActive] BIT NOT NULL
            CONSTRAINT [DF_ProductCategories_IsActive] DEFAULT 1,
        [IsBrowsable] BIT NOT NULL
            CONSTRAINT [DF_ProductCategories_IsBrowsable] DEFAULT 1,
        [LastSyncedAt] DATETIME2 NULL,
        [CreatedAt] DATETIME2 NOT NULL
            CONSTRAINT [DF_ProductCategories_CreatedAt] DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [PK_ProductCategories] PRIMARY KEY ([ProductCategoryId]),
        CONSTRAINT [FK_ProductCategories_Businesses] FOREIGN KEY ([BusinessId])
            REFERENCES [dbo].[Businesses] ([BusinessId]),
        CONSTRAINT [FK_ProductCategories_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
            REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ProductCategories]') AND name = N'IX_ProductCategories_BusinessId')
    CREATE INDEX [IX_ProductCategories_BusinessId]
        ON [dbo].[ProductCategories] ([BusinessId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ProductCategories]') AND name = N'IX_ProductCategories_BusinessId_Connection_ExternalCategoryId')
    CREATE UNIQUE INDEX [IX_ProductCategories_BusinessId_Connection_ExternalCategoryId]
        ON [dbo].[ProductCategories] ([BusinessId], [IntegrationConnectionId], [ExternalCategoryId])
        WHERE [IntegrationConnectionId] IS NOT NULL AND [ExternalCategoryId] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ProductCategories]') AND name = N'IX_ProductCategories_BusinessId_Connection_Name')
    CREATE UNIQUE INDEX [IX_ProductCategories_BusinessId_Connection_Name]
        ON [dbo].[ProductCategories] ([BusinessId], [IntegrationConnectionId], [Name]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ProductCategories]') AND name = N'IX_ProductCategories_Browse')
    CREATE INDEX [IX_ProductCategories_Browse]
        ON [dbo].[ProductCategories] ([BusinessId], [IsActive], [IsBrowsable], [DisplayOrder], [Name]);

IF COL_LENGTH(N'[dbo].[Products]', N'ProductCategoryId') IS NULL
    ALTER TABLE [dbo].[Products] ADD [ProductCategoryId] UNIQUEIDENTIFIER NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'[dbo].[Products]') AND name = N'FK_Products_ProductCategories')
    ALTER TABLE [dbo].[Products] WITH CHECK ADD CONSTRAINT [FK_Products_ProductCategories]
        FOREIGN KEY ([ProductCategoryId]) REFERENCES [dbo].[ProductCategories] ([ProductCategoryId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Products]') AND name = N'IX_Products_ProductCategoryId')
    CREATE INDEX [IX_Products_ProductCategoryId] ON [dbo].[Products] ([ProductCategoryId]);

COMMIT TRANSACTION;
