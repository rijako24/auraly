CREATE TABLE [dbo].[FlowNodeCatalog] (
    [FlowNodeCatalogId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [CatalogKey]        NVARCHAR(100)    NOT NULL,
    [Name]              NVARCHAR(200)    NOT NULL,
    [FlowNodeType]      INT              NOT NULL,
    [Icon]              NVARCHAR(100)    NOT NULL,
    [Category]          NVARCHAR(100)    NULL,
    [Color]             NVARCHAR(50)     NULL,
    [InputsJson]        NVARCHAR(MAX)    NOT NULL CONSTRAINT [DF_FlowNodeCatalog_InputsJson] DEFAULT N'[]',
    [OutputsJson]       NVARCHAR(MAX)    NOT NULL CONSTRAINT [DF_FlowNodeCatalog_OutputsJson] DEFAULT N'[]',
    [ConfigSchemaJson]  NVARCHAR(MAX)    NOT NULL CONSTRAINT [DF_FlowNodeCatalog_ConfigSchemaJson] DEFAULT N'{}',
    [DisplayOrder]      INT              NOT NULL CONSTRAINT [DF_FlowNodeCatalog_DisplayOrder] DEFAULT 0,
    [IsActive]          BIT              NOT NULL CONSTRAINT [DF_FlowNodeCatalog_IsActive] DEFAULT 1,
    [CreatedAt]         DATETIME2        NOT NULL CONSTRAINT [DF_FlowNodeCatalog_CreatedAt] DEFAULT GETUTCDATE(),
    [UpdatedAt]         DATETIME2        NULL,
    CONSTRAINT [UQ_FlowNodeCatalog_CatalogKey] UNIQUE ([CatalogKey])
);

GO

CREATE NONCLUSTERED INDEX [IX_FlowNodeCatalog_IsActive_DisplayOrder]
    ON [dbo].[FlowNodeCatalog] ([IsActive], [DisplayOrder], [Name]);

GO
