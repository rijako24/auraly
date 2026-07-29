CREATE TABLE [dbo].[FiscalIssuerConfigurations]
(
    [FiscalIssuerConfigurationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Version] INT NOT NULL,
    [SupplierTaxId] NVARCHAR(32) NOT NULL,
    [SupplierCheckDigit] NVARCHAR(2) NOT NULL,
    [LegalName] NVARCHAR(256) NOT NULL,
    [TradeName] NVARCHAR(256) NULL,
    [TaxLevelCode] NVARCHAR(64) NOT NULL,
    [TaxSchemeId] NVARCHAR(16) NOT NULL,
    [TaxSchemeName] NVARCHAR(64) NOT NULL,
    [IdentificationTypeCode] NVARCHAR(8) NOT NULL,
    [AddressLine] NVARCHAR(256) NOT NULL,
    [CityCode] NVARCHAR(16) NOT NULL,
    [CityName] NVARCHAR(128) NOT NULL,
    [DepartmentCode] NVARCHAR(16) NOT NULL,
    [DepartmentName] NVARCHAR(128) NOT NULL,
    [PostalZone] NVARCHAR(16) NULL,
    [CountryCode] CHAR(2) NOT NULL CONSTRAINT [DF_FiscalIssuerConfigurations_CountryCode] DEFAULT ('CO'),
    [CountryName] NVARCHAR(64) NOT NULL CONSTRAINT [DF_FiscalIssuerConfigurations_CountryName] DEFAULT ('Colombia'),
    [SoftwareIdentificationCode] NVARCHAR(64) NOT NULL,
    [SoftwarePinSecretReference] NVARCHAR(512) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [TestSetId] UNIQUEIDENTIFIER NULL,
    [CertificateProvider] NVARCHAR(32) NOT NULL,
    [CertificateKeyReference] NVARCHAR(512) NOT NULL,
    [CertificateThumbprint] NVARCHAR(128) NOT NULL,
    [DianEndpoint] NVARCHAR(512) NOT NULL,
    [TechnicalAnnexVersion] NVARCHAR(32) NOT NULL,
    [GeneratorVersion] NVARCHAR(64) NOT NULL,
    [ValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [ValidTo] DATETIMEOFFSET(7) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_FiscalIssuerConfigurations] PRIMARY KEY CLUSTERED ([FiscalIssuerConfigurationId]),
    CONSTRAINT [FK_FiscalIssuerConfigurations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_FiscalIssuerConfigurations_Business_Version] UNIQUE ([BusinessId], [Version]),
    CONSTRAINT [CK_FiscalIssuerConfigurations_Version] CHECK ([Version] > 0),
    CONSTRAINT [CK_FiscalIssuerConfigurations_Environment] CHECK ([Environment] IN (1, 2)),
    CONSTRAINT [CK_FiscalIssuerConfigurations_Validity] CHECK ([ValidTo] IS NULL OR [ValidTo] > [ValidFrom])
);

GO

CREATE UNIQUE INDEX [UX_FiscalIssuerConfigurations_Business_Active]
    ON [dbo].[FiscalIssuerConfigurations] ([BusinessId])
    WHERE [IsActive] = 1;