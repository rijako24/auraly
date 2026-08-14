CREATE TABLE [dbo].[WithholdingRules]
(
    [RuleId] UNIQUEIDENTIFIER NOT NULL,
    [Version] INT NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [Direction] NVARCHAR(16) NOT NULL,
    [Moment] NVARCHAR(16) NOT NULL,
    [BaseKind] NVARCHAR(32) NOT NULL,
    [ConceptCode] NVARCHAR(32) NULL,
    [JurisdictionCode] NVARCHAR(16) NULL,
    [Rate] DECIMAL(9,6) NOT NULL,
    [MinimumBase] DECIMAL(19,4) NOT NULL,
    [RequiredResponsibilities] NVARCHAR(1000) NOT NULL,
    [EffectiveFrom] DATE NOT NULL,
    [EffectiveTo] DATE NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_WithholdingRules] PRIMARY KEY ([RuleId], [Version]),
    CONSTRAINT [FK_WithholdingRules_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_WithholdingRules_Users] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_WithholdingRules_Business_Code_Version] UNIQUE ([BusinessId], [Code], [Version]),
    CONSTRAINT [CK_WithholdingRules_Version] CHECK ([Version] > 0),
    CONSTRAINT [CK_WithholdingRules_Kind] CHECK ([Kind] IN (N'IncomeTax',N'Vat',N'IndustryCommerce')),
    CONSTRAINT [CK_WithholdingRules_Direction] CHECK ([Direction] IN (N'Purchase',N'Sale')),
    CONSTRAINT [CK_WithholdingRules_Moment] CHECK ([Moment] IN (N'Accrual',N'Payment')),
    CONSTRAINT [CK_WithholdingRules_Base] CHECK ([BaseKind] IN (N'TaxExclusiveAmount',N'VatAmount')),
    CONSTRAINT [CK_WithholdingRules_Rate] CHECK ([Rate] > 0 AND [Rate] <= 100),
    CONSTRAINT [CK_WithholdingRules_Minimum] CHECK ([MinimumBase] >= 0),
    CONSTRAINT [CK_WithholdingRules_Effective] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]),
    CONSTRAINT [CK_WithholdingRules_VatBase] CHECK (
      ([Kind]=N'Vat' AND [BaseKind]=N'VatAmount') OR
      ([Kind]<>N'Vat' AND [BaseKind]=N'TaxExclusiveAmount')),
    CONSTRAINT [CK_WithholdingRules_IcaJurisdiction] CHECK ([Kind]<>N'IndustryCommerce' OR [JurisdictionCode] IS NOT NULL)
);
GO

CREATE TABLE [dbo].[CounterpartyTaxProfiles]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CounterpartyId] UNIQUEIDENTIFIER NOT NULL,
    [Responsibilities] NVARCHAR(1000) NOT NULL,
    [JurisdictionCode] NVARCHAR(16) NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CounterpartyTaxProfiles] PRIMARY KEY ([BusinessId],[CounterpartyId]),
    CONSTRAINT [FK_CounterpartyTaxProfiles_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CounterpartyTaxProfiles_Users] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId])
);
GO
