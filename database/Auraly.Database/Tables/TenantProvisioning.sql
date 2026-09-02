CREATE TABLE [dbo].[TenantLegalProfiles]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [LegalName] NVARCHAR(200) NOT NULL,
    [TradeName] NVARCHAR(200) NOT NULL,
    [Nit] NVARCHAR(32) NOT NULL,
    [NormalizedNit] NVARCHAR(32) NOT NULL,
    [VerificationDigit] NVARCHAR(4) NULL,
    [EntityType] NVARCHAR(32) NOT NULL CONSTRAINT [DF_TenantLegalProfiles_EntityType] DEFAULT (N'Organization'),
    [IdentificationTypeCode] NVARCHAR(16) NOT NULL CONSTRAINT [DF_TenantLegalProfiles_IdentificationType] DEFAULT (N'NIT'),
    [LogoMediaRef] NVARCHAR(500) NULL,
    [CountryId] UNIQUEIDENTIFIER NOT NULL,
    [AdministrativeDivisionId] UNIQUEIDENTIFIER NOT NULL,
    [CityId] UNIQUEIDENTIFIER NOT NULL,
    [Address] NVARCHAR(300) NOT NULL,
    [Phone] NVARCHAR(32) NOT NULL,
    [Email] NVARCHAR(254) NOT NULL,
    [TaxResponsibilities] NVARCHAR(500) NOT NULL,
    [PrimaryBusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_TenantLegalProfiles] PRIMARY KEY ([TenantId]),
    CONSTRAINT [FK_TenantLegalProfiles_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_TenantLegalProfiles_Countries] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries]([CountryId]),
    CONSTRAINT [FK_TenantLegalProfiles_Divisions] FOREIGN KEY ([AdministrativeDivisionId]) REFERENCES [dbo].[AdministrativeDivisions]([AdministrativeDivisionId]),
    CONSTRAINT [FK_TenantLegalProfiles_Cities] FOREIGN KEY ([CityId]) REFERENCES [dbo].[Cities]([CityId]),
    CONSTRAINT [FK_TenantLegalProfiles_PrimaryBusiness] FOREIGN KEY ([PrimaryBusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [UQ_TenantLegalProfiles_NormalizedNit] UNIQUE ([IdentificationTypeCode],[NormalizedNit])
    ,CONSTRAINT [CK_TenantLegalProfiles_EntityType] CHECK ([EntityType] IN (N'NaturalPerson',N'Organization'))
    ,CONSTRAINT [CK_TenantLegalProfiles_IdentificationType] CHECK ([IdentificationTypeCode] IN (N'CC',N'CE',N'PA',N'DE',N'PPT',N'NIT'))
    ,CONSTRAINT [CK_TenantLegalProfiles_IdentityCombination] CHECK (
        ([EntityType]=N'NaturalPerson' AND [IdentificationTypeCode] IN (N'CC',N'CE',N'PA',N'DE',N'PPT') AND [VerificationDigit] IS NULL)
        OR ([EntityType]=N'Organization' AND [IdentificationTypeCode]=N'NIT' AND LEN(LTRIM(RTRIM([VerificationDigit])))>0))
);
GO

CREATE TABLE [dbo].[TenantProvisioningRequests]
(
    [ProvisioningRequestId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SalesWarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [OrdersWarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [DefaultCustomerId] UNIQUEIDENTIFIER NOT NULL,
    [AdministratorUserId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_TenantProvisioningRequests] PRIMARY KEY ([ProvisioningRequestId]),
    CONSTRAINT [FK_TenantProvisioningRequests_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_TenantProvisioningRequests_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_TenantProvisioningRequests_Users] FOREIGN KEY ([AdministratorUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_TenantProvisioningRequests_Status] CHECK ([Status] IN (N'Completed',N'Failed'))
);
GO

CREATE TABLE [dbo].[TenantUserInvitations]
(
    [InvitationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NULL,
    [DeliveryEmail] NVARCHAR(254) NOT NULL CONSTRAINT [DF_TenantUserInvitations_DeliveryEmail] DEFAULT (N''),
    [TokenHash] VARBINARY(32) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_TenantUserInvitations] PRIMARY KEY ([InvitationId]),
    CONSTRAINT [FK_TenantUserInvitations_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_TenantUserInvitations_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [UQ_TenantUserInvitations_TokenHash] UNIQUE ([TokenHash]),
    CONSTRAINT [CK_TenantUserInvitations_Status] CHECK ([Status] IN (N'Pending',N'Accepted',N'Expired',N'Revoked'))
);
GO

CREATE TABLE [dbo].[TenantProvisioningOutboxMessages]
(
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Type] NVARCHAR(100) NOT NULL,
    [Payload] NVARCHAR(MAX) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [AvailableAt] DATETIMEOFFSET(7) NOT NULL CONSTRAINT [DF_TenantProvisioningOutbox_AvailableAt] DEFAULT SYSDATETIMEOFFSET(),
    [LeaseId] UNIQUEIDENTIFIER NULL,
    [LeaseExpiresAt] DATETIMEOFFSET(7) NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_TenantProvisioningOutbox_AttemptCount] DEFAULT 0,
    [LastError] NVARCHAR(2000) NULL,
    CONSTRAINT [PK_TenantProvisioningOutboxMessages] PRIMARY KEY ([MessageId]),
    CONSTRAINT [FK_TenantProvisioningOutboxMessages_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId])
);
GO
CREATE INDEX [IX_TenantProvisioningOutbox_Pending] ON [dbo].[TenantProvisioningOutboxMessages]([ProcessedAt],[AvailableAt],[LeaseExpiresAt],[OccurredAt]);
GO
