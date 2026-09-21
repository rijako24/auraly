CREATE TABLE [sales].[InvoiceChargeDefinitions]
(
    [ChargeId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [CurrentVersion] BIGINT NOT NULL,
    CONSTRAINT [PK_InvoiceChargeDefinitions] PRIMARY KEY ([ChargeId]),
    CONSTRAINT [UQ_InvoiceChargeDefinitions_Business_Code] UNIQUE ([BusinessId],[Code]),
    CONSTRAINT [UQ_InvoiceChargeDefinitions_Business_Id] UNIQUE ([BusinessId],[ChargeId]),
    CONSTRAINT [FK_InvoiceChargeDefinitions_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_InvoiceChargeDefinitions_Version] CHECK ([CurrentVersion]>0)
);
GO

CREATE TABLE [sales].[InvoiceChargeVersions]
(
    [ChargeId] UNIQUEIDENTIFIER NOT NULL,
    [Version] BIGINT NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [SortOrder] INT NOT NULL,
    [CalculationCatalog] NVARCHAR(64) NOT NULL CONSTRAINT [DF_InvoiceChargeVersions_CalculationCatalog] DEFAULT N'invoice-charge-calculation',
    [CalculationMode] NVARCHAR(64) NOT NULL,
    [Value] DECIMAL(19,6) NULL,
    [InclusionCatalog] NVARCHAR(64) NOT NULL CONSTRAINT [DF_InvoiceChargeVersions_InclusionCatalog] DEFAULT N'invoice-charge-inclusion',
    [InclusionMode] NVARCHAR(64) NOT NULL,
    [InvoiceAmountLimit] DECIMAL(19,2) NULL,
    [ExpenseConceptId] UNIQUEIDENTIFIER NOT NULL,
    [SalesTaxProfileId] UNIQUEIDENTIFIER NOT NULL,
    [PurchaseTaxProfileId] UNIQUEIDENTIFIER NOT NULL,
    [ExpenseAccountId] UNIQUEIDENTIFIER NOT NULL,
    [CostCenterId] UNIQUEIDENTIFIER NULL,
    [WithholdingConceptCode] NVARCHAR(32) NULL,
    [SalesTaxCode] NVARCHAR(16) NOT NULL,
    [SalesTaxRate] DECIMAL(9,6) NOT NULL,
    [PurchaseTaxRate] DECIMAL(9,6) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [SynchronizationCursor] BIGINT NOT NULL,
    CONSTRAINT [PK_InvoiceChargeVersions] PRIMARY KEY ([ChargeId],[Version]),
    CONSTRAINT [FK_InvoiceChargeVersions_Definition] FOREIGN KEY ([BusinessId],[ChargeId]) REFERENCES [sales].[InvoiceChargeDefinitions]([BusinessId],[ChargeId]),
    CONSTRAINT [FK_InvoiceChargeVersions_Calculation] FOREIGN KEY ([CalculationCatalog],[CalculationMode]) REFERENCES [reference].[Options]([CatalogCode],[Code]),
    CONSTRAINT [FK_InvoiceChargeVersions_Inclusion] FOREIGN KEY ([InclusionCatalog],[InclusionMode]) REFERENCES [reference].[Options]([CatalogCode],[Code]),
    CONSTRAINT [FK_InvoiceChargeVersions_Concept] FOREIGN KEY ([BusinessId],[ExpenseConceptId]) REFERENCES [dbo].[ExpenseConcepts]([BusinessId],[ExpenseConceptId]),
    CONSTRAINT [FK_InvoiceChargeVersions_Tax] FOREIGN KEY ([SalesTaxProfileId]) REFERENCES [dbo].[TaxProfiles]([TaxProfileId]),
    CONSTRAINT [FK_InvoiceChargeVersions_PurchaseTax] FOREIGN KEY ([PurchaseTaxProfileId]) REFERENCES [dbo].[TaxProfiles]([TaxProfileId]),
    CONSTRAINT [FK_InvoiceChargeVersions_ExpenseAccount] FOREIGN KEY ([ExpenseAccountId]) REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    CONSTRAINT [FK_InvoiceChargeVersions_CostCenter] FOREIGN KEY ([CostCenterId]) REFERENCES [dbo].[AccountingCostCenters]([CostCenterId]),
    CONSTRAINT [CK_InvoiceChargeVersions_TaxRates] CHECK ([SalesTaxRate] BETWEEN 0 AND 100 AND [PurchaseTaxRate] BETWEEN 0 AND 100),
    CONSTRAINT [FK_InvoiceChargeVersions_User] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_InvoiceChargeVersions_Values] CHECK ([Version]>0 AND [SortOrder]>=0 AND [Value]>=0 AND [InvoiceAmountLimit]>=0 AND [SynchronizationCursor]>=0),
    CONSTRAINT [CK_InvoiceChargeVersions_Catalogs] CHECK ([CalculationCatalog]=N'invoice-charge-calculation' AND [InclusionCatalog]=N'invoice-charge-inclusion'),
    CONSTRAINT [CK_InvoiceChargeVersions_Mode] CHECK
      (([CalculationMode]=N'Manual' AND ([Value] IS NULL OR [Value]=ROUND([Value],2))) OR
       ([CalculationMode]=N'Ranges' AND [Value] IS NULL) OR
       ([CalculationMode]=N'Fixed' AND [Value] IS NOT NULL AND [Value]=ROUND([Value],2)) OR
       ([CalculationMode]=N'Percentage' AND [Value] IS NOT NULL AND [Value] BETWEEN 0 AND 100)),
    CONSTRAINT [CK_InvoiceChargeVersions_Threshold] CHECK
      (([InclusionMode]=N'UpToInvoiceAmount' AND [InvoiceAmountLimit] IS NOT NULL) OR
       ([InclusionMode] IN (N'Always',N'Never') AND [InvoiceAmountLimit] IS NULL))
);
GO

CREATE TABLE [sales].[InvoiceChargeRanges]
(
    [ChargeId] UNIQUEIDENTIFIER NOT NULL,
    [Version] BIGINT NOT NULL,
    [Position] INT NOT NULL,
    [FromInclusive] DECIMAL(19,2) NOT NULL,
    [ToExclusive] DECIMAL(19,2) NULL,
    [CalculationCatalog] NVARCHAR(64) NOT NULL CONSTRAINT [DF_InvoiceChargeRanges_Catalog] DEFAULT N'invoice-charge-calculation',
    [CalculationMode] NVARCHAR(64) NOT NULL,
    [Value] DECIMAL(19,6) NOT NULL,
    CONSTRAINT [PK_InvoiceChargeRanges] PRIMARY KEY ([ChargeId],[Version],[Position]),
    CONSTRAINT [FK_InvoiceChargeRanges_Version] FOREIGN KEY ([ChargeId],[Version]) REFERENCES [sales].[InvoiceChargeVersions]([ChargeId],[Version]),
    CONSTRAINT [FK_InvoiceChargeRanges_Mode] FOREIGN KEY ([CalculationCatalog],[CalculationMode]) REFERENCES [reference].[Options]([CatalogCode],[Code]),
    CONSTRAINT [CK_InvoiceChargeRanges_Mode] CHECK ([CalculationCatalog]=N'invoice-charge-calculation' AND [CalculationMode] IN (N'Fixed',N'Percentage')),
    CONSTRAINT [CK_InvoiceChargeRanges_Values] CHECK ([Position] BETWEEN 1 AND 32 AND [FromInclusive]>=0 AND ([ToExclusive] IS NULL OR [ToExclusive]>[FromInclusive]) AND [Value]>=0 AND
      (([CalculationMode]=N'Fixed' AND [Value]=ROUND([Value],2)) OR ([CalculationMode]=N'Percentage' AND [Value]<=100)))
);
GO

CREATE TABLE [sales].[InvoiceChargeSuppliers]
(
    [ChargeId] UNIQUEIDENTIFIER NOT NULL,
    [Version] BIGINT NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(300) NOT NULL,
    [Identification] NVARCHAR(64) NULL,
    [DefaultPaymentDueDays] INT NOT NULL,
    [IsActive] BIT NOT NULL,
    [AppliesWithholding] BIT NOT NULL,
    [TaxResponsibilitiesJson] NVARCHAR(MAX) NULL,
    [TaxJurisdictionCode] NVARCHAR(32) NULL,
    [PurchaseEvidencePolicy] NVARCHAR(64) NULL,
    CONSTRAINT [CK_InvoiceChargeSuppliers_Responsibilities] CHECK ([TaxResponsibilitiesJson] IS NULL OR ISJSON([TaxResponsibilitiesJson])=1),
    CONSTRAINT [PK_InvoiceChargeSuppliers] PRIMARY KEY ([ChargeId],[Version],[SupplierId]),
    CONSTRAINT [FK_InvoiceChargeSuppliers_Version] FOREIGN KEY ([ChargeId],[Version]) REFERENCES [sales].[InvoiceChargeVersions]([ChargeId],[Version]),
    CONSTRAINT [FK_InvoiceChargeSuppliers_Supplier] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers]([SupplierId])
);
GO

CREATE TABLE [sales].[InvoiceChargeDraftSelections]
(
    [DraftId] UNIQUEIDENTIFIER NOT NULL,
    [AppliedChargeId] UNIQUEIDENTIFIER NOT NULL,
    [ChargeId] UNIQUEIDENTIFIER NOT NULL,
    [Version] BIGINT NOT NULL,
    [SelectionJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_InvoiceChargeDraftSelections] PRIMARY KEY ([DraftId],[AppliedChargeId]),
    CONSTRAINT [FK_InvoiceChargeDraftSelections_Draft] FOREIGN KEY ([DraftId]) REFERENCES [dbo].[SalesDrafts]([SalesDraftId]),
    CONSTRAINT [FK_InvoiceChargeDraftSelections_Version] FOREIGN KEY ([ChargeId],[Version]) REFERENCES [sales].[InvoiceChargeVersions]([ChargeId],[Version]),
    CONSTRAINT [CK_InvoiceChargeDraftSelections_Json] CHECK (ISJSON([SelectionJson])=1)
);
GO
