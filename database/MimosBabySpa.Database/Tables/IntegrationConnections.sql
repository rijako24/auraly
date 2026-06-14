CREATE TABLE [dbo].[IntegrationConnections] (
    [IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Provider] INT NOT NULL,
    [Capability] INT NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [AccountIdentifier] NVARCHAR(300) NULL,
    [SettingsJson] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
    [SecretsJson] NVARCHAR(MAX) NULL,
    [IsEnabled] BIT NOT NULL DEFAULT 0,
    [LastSyncAt] DATETIME2 NULL,
    [LastError] NVARCHAR(4000) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_IntegrationConnections_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_IntegrationConnections_Provider] CHECK ([Provider] IN (0, 1)),
    CONSTRAINT [CK_IntegrationConnections_Capability] CHECK ([Capability] IN (0, 1))
);

GO

CREATE UNIQUE INDEX [IX_IntegrationConnections_BusinessId_Provider_Capability]
    ON [dbo].[IntegrationConnections] ([BusinessId], [Provider], [Capability]);

GO

CREATE INDEX [IX_IntegrationConnections_BusinessId]
    ON [dbo].[IntegrationConnections] ([BusinessId]);

GO
