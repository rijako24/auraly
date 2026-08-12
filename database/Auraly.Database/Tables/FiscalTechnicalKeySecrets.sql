CREATE TABLE [dbo].[FiscalTechnicalKeySecrets]
(
    [FiscalTechnicalKeySecretId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NOT NULL,
    [TechnicalKeyVersion] NVARCHAR(64) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [ProtectedValue] VARBINARY(MAX) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalTechnicalKeySecrets] PRIMARY KEY ([FiscalTechnicalKeySecretId]),
    CONSTRAINT [FK_FiscalTechnicalKeySecrets_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_FiscalTechnicalKeySecrets_Authorizations] FOREIGN KEY ([FiscalAuthorizationId]) REFERENCES [dbo].[FiscalAuthorizations] ([FiscalAuthorizationId]),
    CONSTRAINT [UQ_FiscalTechnicalKeySecrets_Reference] UNIQUE ([BusinessId],[FiscalAuthorizationId],[TechnicalKeyVersion],[Environment]),
    CONSTRAINT [CK_FiscalTechnicalKeySecrets_Environment] CHECK ([Environment] IN (1,2))
);
