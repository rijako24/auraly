CREATE TABLE [dbo].[IntegrationConnections] (
    [IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ConnectionType] INT NOT NULL DEFAULT 0,
    [Provider] INT NOT NULL,
    [Capability] INT NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [AccountIdentifier] NVARCHAR(300) NULL,
    [SettingsJson] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
    [SecretsJson] NVARCHAR(MAX) NULL,
    [IsEnabled] BIT NOT NULL DEFAULT 0,
    [LastSyncAt] DATETIME2 NULL,
    [LastError] NVARCHAR(4000) NULL,
    [CatalogSyncNextPage] INT NOT NULL CONSTRAINT [DF_IntegrationConnections_CatalogSyncNextPage] DEFAULT 1,
    [CatalogDeltaCursorDate] DATE NULL,
    [CustomerSyncNextPage] INT NOT NULL CONSTRAINT [DF_IntegrationConnections_CustomerSyncNextPage] DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_IntegrationConnections_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_IntegrationConnections_ConnectionType] CHECK ([ConnectionType] IN (0, 1)),
    CONSTRAINT [CK_IntegrationConnections_CatalogSyncNextPage] CHECK ([CatalogSyncNextPage] >= 0),
    CONSTRAINT [CK_IntegrationConnections_CustomerSyncNextPage] CHECK ([CustomerSyncNextPage] >= 1)
);

GO

CREATE UNIQUE INDEX [IX_IntegrationConnections_BusinessId_Type_Provider_Capability]
    ON [dbo].[IntegrationConnections] ([BusinessId], [ConnectionType], [Provider], [Capability]);

GO

CREATE INDEX [IX_IntegrationConnections_BusinessId]
    ON [dbo].[IntegrationConnections] ([BusinessId]);

GO
