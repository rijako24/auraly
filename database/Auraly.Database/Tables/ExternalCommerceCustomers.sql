CREATE TABLE [dbo].[ExternalCommerceCustomers] (
    [ExternalCommerceCustomerId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,
    [ExternalAccountId] NVARCHAR(150) NOT NULL,
    [ExternalCustomerId] NVARCHAR(150) NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [ReconciliationStatus] NVARCHAR(16) NOT NULL CONSTRAINT [DF_ExternalCommerceCustomers_ReconciliationStatus] DEFAULT N'Pending',
    [ReconciliationError] NVARCHAR(500) NULL,
    [ReconciledAt] DATETIME2 NULL,
    [ReconciledBy] UNIQUEIDENTIFIER NULL,
    [ReconciliationOrigin] NVARCHAR(16) NULL,
    [Name] NVARCHAR(250) NULL,
    [PhoneNormalized] NVARCHAR(50) NOT NULL,
    [Phone] NVARCHAR(50) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [LastSyncedAt] DATETIME2 NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ExternalCommerceCustomers_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_ExternalCommerceCustomers_IntegrationConnections]
        FOREIGN KEY ([IntegrationConnectionId]) REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId]),
    CONSTRAINT [FK_ExternalCommerceCustomers_Parties]
        FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId]),
    CONSTRAINT [FK_ExternalCommerceCustomers_Customers]
        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_ExternalCommerceCustomers_ReconciledBy]
        FOREIGN KEY ([ReconciledBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_ExternalCommerceCustomers_ReconciliationStatus]
        CHECK ([ReconciliationStatus] IN (N'Pending', N'Linked', N'Conflict')),
    CONSTRAINT [CK_ExternalCommerceCustomers_ReconciliationOrigin] CHECK ([ReconciliationOrigin] IS NULL OR [ReconciliationOrigin] IN (N'Manual', N'Integration')),
    CONSTRAINT [CK_ExternalCommerceCustomers_Link] CHECK (
        ([PartyId] IS NULL AND [CustomerId] IS NULL) OR ([PartyId] IS NOT NULL AND [CustomerId] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_ExternalCommerceCustomers_ExternalKeys]
    ON [dbo].[ExternalCommerceCustomers]
       ([BusinessId], [IntegrationConnectionId], [ExternalAccountId], [ExternalCustomerId]);
GO

CREATE INDEX [IX_ExternalCommerceCustomers_Phone]
    ON [dbo].[ExternalCommerceCustomers]
       ([BusinessId], [IntegrationConnectionId], [PhoneNormalized], [IsActive]);
GO

CREATE INDEX [IX_ExternalCommerceCustomers_Business_Status_LastSynced]
    ON [dbo].[ExternalCommerceCustomers] ([BusinessId], [ReconciliationStatus], [LastSyncedAt], [ExternalCommerceCustomerId]);
GO
