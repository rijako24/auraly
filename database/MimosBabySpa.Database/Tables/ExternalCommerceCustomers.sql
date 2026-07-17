CREATE TABLE [dbo].[ExternalCommerceCustomers] (
    [ExternalCommerceCustomerId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,
    [ExternalAccountId] NVARCHAR(150) NOT NULL,
    [ExternalCustomerId] NVARCHAR(150) NOT NULL,
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
        FOREIGN KEY ([IntegrationConnectionId]) REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
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
