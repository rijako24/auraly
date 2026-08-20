CREATE TABLE [reference].[Options]
(
    [OptionId] UNIQUEIDENTIFIER NOT NULL,
    [CatalogCode] NVARCHAR(64) NOT NULL,
    [Code] NVARCHAR(64) NOT NULL,
    [Label] NVARCHAR(160) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ReferenceOptions_IsActive] DEFAULT 1,
    [SortOrder] INT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ReferenceOptions] PRIMARY KEY CLUSTERED ([OptionId]),
    CONSTRAINT [UQ_ReferenceOptions_Catalog_Code]
        UNIQUE ([CatalogCode],[Code]),
    CONSTRAINT [CK_ReferenceOptions_CatalogCode]
        CHECK (LEN([CatalogCode]) > 0 AND [CatalogCode] NOT LIKE N'%[^a-z0-9_-]%'),
    CONSTRAINT [CK_ReferenceOptions_Code]
        CHECK (LEN(LTRIM(RTRIM([Code]))) > 0),
    CONSTRAINT [CK_ReferenceOptions_Label]
        CHECK (LEN(LTRIM(RTRIM([Label]))) > 0),
    CONSTRAINT [CK_ReferenceOptions_SortOrder]
        CHECK ([SortOrder] >= 0)
);

GO

CREATE INDEX [IX_ReferenceOptions_ActiveCatalog]
    ON [reference].[Options] ([CatalogCode],[IsActive],[SortOrder])
    INCLUDE ([Code],[Label],[Description]);
