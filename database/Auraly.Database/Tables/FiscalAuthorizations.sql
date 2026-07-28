CREATE TABLE [dbo].[FiscalAuthorizations]
(
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorizationNumber] NVARCHAR(64) NOT NULL,
    [SupplierTaxId] NVARCHAR(32) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [QrValidationUrl] NVARCHAR(500) NOT NULL,
    [ValidFrom] DATE NOT NULL,
    [ValidUntil] DATE NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalAuthorizations] PRIMARY KEY CLUSTERED ([FiscalAuthorizationId]),
    CONSTRAINT [FK_FiscalAuthorizations_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_FiscalAuthorizations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_FiscalAuthorizations_Tenant_Business_Number] UNIQUE ([TenantId], [BusinessId], [AuthorizationNumber]),
    CONSTRAINT [CK_FiscalAuthorizations_Environment] CHECK ([Environment] IN (1, 2)),
    CONSTRAINT [CK_FiscalAuthorizations_Validity] CHECK ([ValidUntil] >= [ValidFrom])
);

GO

CREATE INDEX [IX_FiscalAuthorizations_Tenant_Business]
    ON [dbo].[FiscalAuthorizations] ([TenantId], [BusinessId]);

