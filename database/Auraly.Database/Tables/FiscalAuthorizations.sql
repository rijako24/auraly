CREATE TABLE [dbo].[FiscalAuthorizations]
(
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorizationNumber] NVARCHAR(64) NOT NULL,
    [SupplierTaxId] NVARCHAR(32) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [QrValidationUrl] NVARCHAR(500) NOT NULL,
    [TechnicalKeyVersion] NVARCHAR(64) NOT NULL CONSTRAINT [DF_FiscalAuthorizations_TechnicalKeyVersion] DEFAULT N'v1',
    [ValidFrom] DATE NOT NULL,
    [ValidUntil] DATE NOT NULL,
    [AuthorizedRangeStart] BIGINT NULL,
    [AuthorizedRangeEnd] BIGINT NULL,
    [InitialConsecutive] BIGINT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalAuthorizations] PRIMARY KEY CLUSTERED ([FiscalAuthorizationId]),
    CONSTRAINT [FK_FiscalAuthorizations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_FiscalAuthorizations_Business_Number] UNIQUE ([BusinessId], [AuthorizationNumber]),
    CONSTRAINT [CK_FiscalAuthorizations_Environment] CHECK ([Environment] IN (1, 2)),
    CONSTRAINT [CK_FiscalAuthorizations_Validity] CHECK ([ValidUntil] >= [ValidFrom]),
    CONSTRAINT [CK_FiscalAuthorizations_Range] CHECK (
        ([AuthorizedRangeStart] IS NULL AND [AuthorizedRangeEnd] IS NULL AND [InitialConsecutive] IS NULL)
        OR ([AuthorizedRangeStart] > 0
            AND [AuthorizedRangeEnd] >= [AuthorizedRangeStart]
            AND [InitialConsecutive] BETWEEN [AuthorizedRangeStart] AND [AuthorizedRangeEnd]))
);

GO

CREATE INDEX [IX_FiscalAuthorizations_Business]
    ON [dbo].[FiscalAuthorizations] ([BusinessId]);

