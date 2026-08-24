CREATE TABLE [fiscal].[FiscalCredentialSecrets]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProtectedSoftwarePin] VARBINARY(MAX) NOT NULL,
    [ProtectedCertificatePfx] VARBINARY(MAX) NOT NULL,
    [CertificateThumbprint] NVARCHAR(128) NOT NULL,
    [CertificateValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [CertificateValidTo] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_FiscalCredentialSecrets] PRIMARY KEY ([TenantId]),
    CONSTRAINT [FK_FiscalCredentialSecrets_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [CK_FiscalCredentialSecrets_Validity] CHECK ([CertificateValidTo] > [CertificateValidFrom])
);
GO
