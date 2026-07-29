CREATE TABLE [dbo].[Countries] (
    [CountryId] UNIQUEIDENTIFIER NOT NULL,
    [Code] CHAR(2) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Countries] PRIMARY KEY ([CountryId]),
    CONSTRAINT [UQ_Countries_Code] UNIQUE ([Code])
);
GO
CREATE INDEX [IX_Countries_Name] ON [dbo].[Countries] ([Name]);
GO

CREATE TABLE [dbo].[AdministrativeDivisions] (
    [AdministrativeDivisionId] UNIQUEIDENTIFIER NOT NULL,
    [CountryId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(16) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [DivisionType] NVARCHAR(24) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AdministrativeDivisions] PRIMARY KEY ([AdministrativeDivisionId]),
    CONSTRAINT [FK_AdministrativeDivisions_Countries] FOREIGN KEY ([CountryId])
        REFERENCES [dbo].[Countries] ([CountryId]),
    CONSTRAINT [UQ_AdministrativeDivisions_Country_Code] UNIQUE ([CountryId], [Code])
);
GO
CREATE INDEX [IX_AdministrativeDivisions_Country_Name]
    ON [dbo].[AdministrativeDivisions] ([CountryId], [Name]);
GO

CREATE TABLE [dbo].[Cities] (
    [CityId] UNIQUEIDENTIFIER NOT NULL,
    [AdministrativeDivisionId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(16) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Cities] PRIMARY KEY ([CityId]),
    CONSTRAINT [FK_Cities_AdministrativeDivisions] FOREIGN KEY ([AdministrativeDivisionId])
        REFERENCES [dbo].[AdministrativeDivisions] ([AdministrativeDivisionId]),
    CONSTRAINT [UQ_Cities_Division_Code] UNIQUE ([AdministrativeDivisionId], [Code])
);
GO
CREATE INDEX [IX_Cities_Division_Name]
    ON [dbo].[Cities] ([AdministrativeDivisionId], [Name]);
GO

CREATE TABLE [dbo].[Parties] (
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PartyType] NVARCHAR(24) NOT NULL,
    [IdentificationCountryId] UNIQUEIDENTIFIER NULL,
    [IdentificationTypeCode] NVARCHAR(16) NULL,
    [Identification] NVARCHAR(64) NULL,
    [NormalizedIdentification] NVARCHAR(64) NULL,
    [VerificationDigit] NVARCHAR(4) NULL,
    [DisplayName] NVARCHAR(200) NULL,
    [LegalName] NVARCHAR(200) NULL,
    [FirstName] NVARCHAR(100) NULL,
    [LastName] NVARCHAR(100) NULL,
    [CompletionStatus] NVARCHAR(16) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Parties] PRIMARY KEY ([PartyId]),
    CONSTRAINT [UQ_Parties_Tenant_Party] UNIQUE ([TenantId], [PartyId]),
    CONSTRAINT [FK_Parties_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_Parties_IdentificationCountry] FOREIGN KEY ([IdentificationCountryId])
        REFERENCES [dbo].[Countries] ([CountryId]),
    CONSTRAINT [CK_Parties_Type] CHECK ([PartyType] IN (N'NaturalPerson', N'Organization')),
    CONSTRAINT [CK_Parties_CompletionStatus] CHECK ([CompletionStatus] IN (N'Complete', N'Incomplete')),
    CONSTRAINT [CK_Parties_IdentificationCompleteness] CHECK (
        ([IdentificationCountryId] IS NULL AND [IdentificationTypeCode] IS NULL
         AND [Identification] IS NULL AND [NormalizedIdentification] IS NULL)
        OR
        ([IdentificationCountryId] IS NOT NULL AND [IdentificationTypeCode] IS NOT NULL
         AND [Identification] IS NOT NULL AND [NormalizedIdentification] IS NOT NULL))
);
GO
CREATE UNIQUE INDEX [UX_Parties_Tenant_Identification] ON [dbo].[Parties]
    ([TenantId], [IdentificationCountryId], [IdentificationTypeCode], [NormalizedIdentification])
    WHERE [NormalizedIdentification] IS NOT NULL;
GO
CREATE INDEX [IX_Parties_Tenant_DisplayName] ON [dbo].[Parties] ([TenantId], [DisplayName]);
GO

CREATE TABLE [dbo].[PartyContacts] (
    [PartyContactId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [ContactType] NVARCHAR(16) NOT NULL,
    [Value] NVARCHAR(254) NOT NULL,
    [NormalizedValue] NVARCHAR(254) NOT NULL,
    [IsPrimary] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PartyContacts] PRIMARY KEY ([PartyContactId]),
    CONSTRAINT [FK_PartyContacts_Parties] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId]),
    CONSTRAINT [CK_PartyContacts_Type] CHECK ([ContactType] IN (N'Email', N'Phone')),
    CONSTRAINT [UQ_PartyContacts_Party_Type_Value] UNIQUE ([PartyId], [ContactType], [NormalizedValue])
);
GO
CREATE UNIQUE INDEX [UX_PartyContacts_Primary]
    ON [dbo].[PartyContacts] ([PartyId], [ContactType])
    WHERE [IsPrimary] = 1 AND [IsActive] = 1;
GO

CREATE TABLE [dbo].[Customers] (
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([CustomerId]),
    CONSTRAINT [FK_Customers_Parties] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId]),
    CONSTRAINT [FK_Customers_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_Customers_Party_Business] UNIQUE ([PartyId], [BusinessId])
);
GO
CREATE INDEX [IX_Customers_Business_Party] ON [dbo].[Customers] ([BusinessId], [PartyId]);
GO

CREATE TABLE [dbo].[PartySites] (
    [PartySiteId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [CountryId] UNIQUEIDENTIFIER NOT NULL,
    [AdministrativeDivisionId] UNIQUEIDENTIFIER NOT NULL,
    [CityId] UNIQUEIDENTIFIER NOT NULL,
    [AddressLine] NVARCHAR(300) NOT NULL,
    [Neighborhood] NVARCHAR(120) NULL,
    [PostalCode] NVARCHAR(16) NULL,
    [Email] NVARCHAR(254) NULL,
    [Phone] NVARCHAR(32) NULL,
    [IsPrimary] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PartySites] PRIMARY KEY ([PartySiteId]),
    CONSTRAINT [FK_PartySites_Parties] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId]),
    CONSTRAINT [FK_PartySites_Countries] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([CountryId]),
    CONSTRAINT [FK_PartySites_AdministrativeDivisions] FOREIGN KEY ([AdministrativeDivisionId])
        REFERENCES [dbo].[AdministrativeDivisions] ([AdministrativeDivisionId]),
    CONSTRAINT [FK_PartySites_Cities] FOREIGN KEY ([CityId]) REFERENCES [dbo].[Cities] ([CityId]),
    CONSTRAINT [UQ_PartySites_Party_Code] UNIQUE ([PartyId], [Code])
);
GO
CREATE UNIQUE INDEX [UX_PartySites_Primary]
    ON [dbo].[PartySites] ([PartyId]) WHERE [IsPrimary] = 1 AND [IsActive] = 1;
GO

CREATE TABLE [dbo].[CustomerPricingSettings] (
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [PriceListId] UNIQUEIDENTIFIER NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CustomerPricingSettings] PRIMARY KEY ([CustomerId]),
    CONSTRAINT [FK_CustomerPricingSettings_Customers] FOREIGN KEY ([CustomerId])
        REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_CustomerPricingSettings_PriceLists] FOREIGN KEY ([PriceListId])
        REFERENCES [dbo].[PriceLists] ([PriceListId]),
    CONSTRAINT [FK_CustomerPricingSettings_PriceChannels] FOREIGN KEY ([PriceChannelId])
        REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [CK_CustomerPricingSettings_Exclusive] CHECK (
        NOT ([PriceListId] IS NOT NULL AND [PriceChannelId] IS NOT NULL))
);
GO

CREATE TABLE [dbo].[CustomerCreationReceipts] (
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OperationId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_CustomerCreationReceipts] PRIMARY KEY ([BusinessId], [OperationId]),
    CONSTRAINT [FK_CustomerCreationReceipts_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CustomerCreationReceipts_Customers] FOREIGN KEY ([CustomerId])
        REFERENCES [dbo].[Customers] ([CustomerId])
);
GO

CREATE TABLE [dbo].[PartySiteCreationReceipts] (
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OperationId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [PartySiteId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PartySiteCreationReceipts] PRIMARY KEY ([BusinessId], [OperationId]),
    CONSTRAINT [FK_PartySiteCreationReceipts_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_PartySiteCreationReceipts_Customers] FOREIGN KEY ([CustomerId])
        REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_PartySiteCreationReceipts_PartySites] FOREIGN KEY ([PartySiteId])
        REFERENCES [dbo].[PartySites] ([PartySiteId])
);
GO
