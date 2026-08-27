CREATE TABLE [payroll].[CatalogOptions]
(
    [OptionId] UNIQUEIDENTIFIER NOT NULL,
    [CatalogCode] NVARCHAR(64) NOT NULL,
    [Code] NVARCHAR(64) NOT NULL,
    [Label] NVARCHAR(160) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [MetadataCode] NVARCHAR(64) NULL,
    [DianCode] NVARCHAR(64) NULL,
    [IsActive] BIT NOT NULL,
    [SortOrder] INT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_PayrollCatalogOptions] PRIMARY KEY ([OptionId]),
    CONSTRAINT [UQ_PayrollCatalogOptions_Catalog_Code] UNIQUE ([CatalogCode],[Code]),
    CONSTRAINT [CK_PayrollCatalogOptions_Order] CHECK ([SortOrder] BETWEEN 0 AND 9999)
);
GO
CREATE INDEX [IX_PayrollCatalogOptions_Catalog_Active_Order]
    ON [payroll].[CatalogOptions]([CatalogCode],[IsActive],[SortOrder]) INCLUDE ([Code],[Label]);
GO

CREATE TABLE [payroll].[RuleSets]
(
    [RuleSetId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NULL,
    [CountryCode] CHAR(2) NOT NULL,
    [Code] NVARCHAR(64) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [EffectiveFrom] DATE NOT NULL,
    [EffectiveTo] DATE NULL,
    [SourceReference] NVARCHAR(500) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ApprovedBy] UNIQUEIDENTIFIER NULL,
    [ApprovedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollRuleSets] PRIMARY KEY ([RuleSetId]),
    CONSTRAINT [FK_PayrollRuleSets_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [CK_PayrollRuleSets_Range] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]),
    CONSTRAINT [CK_PayrollRuleSets_Status] CHECK ([Status] IN (N'Draft',N'Approved',N'Retired'))
);
GO

CREATE TABLE [payroll].[Settings]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [IsEmployerExemptFromHealthSenaIcbf] BIT NOT NULL,
    [ElectronicPayrollEnabled] BIT NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollSettings] PRIMARY KEY ([TenantId]),
    CONSTRAINT [FK_PayrollSettings_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId])
);
GO
CREATE UNIQUE INDEX [UX_PayrollRuleSets_Global_Code_From]
    ON [payroll].[RuleSets]([CountryCode],[Code],[EffectiveFrom]) WHERE [TenantId] IS NULL;
GO
CREATE UNIQUE INDEX [UX_PayrollRuleSets_Tenant_Code_From]
    ON [payroll].[RuleSets]([TenantId],[Code],[EffectiveFrom]) WHERE [TenantId] IS NOT NULL;
GO

CREATE TABLE [payroll].[RuleParameters]
(
    [RuleParameterId] UNIQUEIDENTIFIER NOT NULL,
    [RuleSetId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(64) NOT NULL,
    [NumericValue] DECIMAL(19,8) NOT NULL,
    [UnitCode] NVARCHAR(32) NOT NULL,
    [Description] NVARCHAR(300) NULL,
    CONSTRAINT [PK_PayrollRuleParameters] PRIMARY KEY ([RuleParameterId]),
    CONSTRAINT [FK_PayrollRuleParameters_RuleSets] FOREIGN KEY ([RuleSetId]) REFERENCES [payroll].[RuleSets]([RuleSetId]),
    CONSTRAINT [UQ_PayrollRuleParameters_Set_Code] UNIQUE ([RuleSetId],[Code])
);
GO

CREATE TABLE [payroll].[Concepts]
(
    [ConceptId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [NatureOptionId] UNIQUEIDENTIFIER NOT NULL,
    [CalculationMethodOptionId] UNIQUEIDENTIFIER NOT NULL,
    [TreatmentOptionId] UNIQUEIDENTIFIER NOT NULL,
    [DianConceptOptionId] UNIQUEIDENTIFIER NULL,
    [AccountingCategoryOptionId] UNIQUEIDENTIFIER NOT NULL,
    [SystemRoleOptionId] UNIQUEIDENTIFIER NULL,
    [IsSalaryBase] BIT NOT NULL,
    [IsSocialSecurityBase] BIT NOT NULL,
    [IsBenefitsBase] BIT NOT NULL,
    [IsTaxWithholdingBase] BIT NOT NULL,
    [RequiresDeductionAgreement] BIT NOT NULL,
    [EffectiveFrom] DATE NOT NULL,
    [EffectiveTo] DATE NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollConcepts] PRIMARY KEY ([ConceptId]),
    CONSTRAINT [UQ_PayrollConcepts_Tenant_Concept] UNIQUE ([TenantId],[ConceptId]),
    CONSTRAINT [UQ_PayrollConcepts_Tenant_Code] UNIQUE ([TenantId],[Code]),
    CONSTRAINT [FK_PayrollConcepts_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PayrollConcepts_Nature] FOREIGN KEY ([NatureOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollConcepts_Method] FOREIGN KEY ([CalculationMethodOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollConcepts_Treatment] FOREIGN KEY ([TreatmentOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollConcepts_Dian] FOREIGN KEY ([DianConceptOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollConcepts_Accounting] FOREIGN KEY ([AccountingCategoryOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollConcepts_SystemRole] FOREIGN KEY ([SystemRoleOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [CK_PayrollConcepts_Range] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom])
);
GO
CREATE INDEX [IX_PayrollConcepts_Tenant_Active_Name]
    ON [payroll].[Concepts]([TenantId],[IsActive],[Name]);
GO
CREATE UNIQUE INDEX [UX_PayrollConcepts_Tenant_SystemRole]
    ON [payroll].[Concepts]([TenantId],[SystemRoleOptionId]) WHERE [SystemRoleOptionId] IS NOT NULL;
GO

CREATE TABLE [payroll].[Employments]
(
    [EmploymentId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [EmployeeId] UNIQUEIDENTIFIER NULL,
    [ContractTypeOptionId] UNIQUEIDENTIFIER NOT NULL,
    [SalaryTypeOptionId] UNIQUEIDENTIFIER NOT NULL,
    [PayFrequencyOptionId] UNIQUEIDENTIFIER NOT NULL,
    [RiskClassOptionId] UNIQUEIDENTIFIER NOT NULL,
    [WorkerTypeOptionId] UNIQUEIDENTIFIER NOT NULL,
    [WorkerSubtypeOptionId] UNIQUEIDENTIFIER NULL,
    [PaymentMethodOptionId] UNIQUEIDENTIFIER NOT NULL,
    [ContractNumber] NVARCHAR(64) NOT NULL,
    [StartDate] DATE NOT NULL,
    [EndDate] DATE NULL,
    [MonthlySalary] DECIMAL(19,4) NOT NULL,
    [IntegralSalaryPercentage] DECIMAL(9,6) NULL,
    [BankAccountReference] NVARCHAR(200) NULL,
    [BankOptionId] UNIQUEIDENTIFIER NULL,
    [BankAccountTypeOptionId] UNIQUEIDENTIFIER NULL,
    [BankAccountNumber] NVARCHAR(64) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollEmployments] PRIMARY KEY ([EmploymentId]),
    CONSTRAINT [UQ_PayrollEmployments_Tenant_Employment] UNIQUE ([TenantId],[EmploymentId]),
    CONSTRAINT [UQ_PayrollEmployments_Tenant_Contract] UNIQUE ([TenantId],[ContractNumber]),
    CONSTRAINT [FK_PayrollEmployments_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PayrollEmployments_Party] FOREIGN KEY ([TenantId],[PartyId]) REFERENCES [dbo].[Parties]([TenantId],[PartyId]),
    CONSTRAINT [FK_PayrollEmployments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PayrollEmployments_Employees] FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees]([EmployeeId]),
    CONSTRAINT [FK_PayrollEmployments_ContractType] FOREIGN KEY ([ContractTypeOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_SalaryType] FOREIGN KEY ([SalaryTypeOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_PayFrequency] FOREIGN KEY ([PayFrequencyOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_RiskClass] FOREIGN KEY ([RiskClassOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_WorkerType] FOREIGN KEY ([WorkerTypeOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_WorkerSubtype] FOREIGN KEY ([WorkerSubtypeOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_PaymentMethod] FOREIGN KEY ([PaymentMethodOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_Bank] FOREIGN KEY ([BankOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollEmployments_BankAccountType] FOREIGN KEY ([BankAccountTypeOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [CK_PayrollEmployments_Dates] CHECK ([EndDate] IS NULL OR [EndDate] >= [StartDate]),
    CONSTRAINT [CK_PayrollEmployments_Salary] CHECK ([MonthlySalary] > 0),
    CONSTRAINT [CK_PayrollEmployments_IntegralPercentage] CHECK ([IntegralSalaryPercentage] IS NULL OR [IntegralSalaryPercentage] BETWEEN 0 AND 1)
    ,CONSTRAINT [CK_PayrollEmployments_BankAccount] CHECK (
      ([BankOptionId] IS NULL AND [BankAccountTypeOptionId] IS NULL AND [BankAccountNumber] IS NULL)
      OR ([BankOptionId] IS NOT NULL AND [BankAccountTypeOptionId] IS NOT NULL
          AND LEN(LTRIM(RTRIM([BankAccountNumber]))) BETWEEN 4 AND 64))
);
GO
CREATE UNIQUE INDEX [UX_PayrollEmployments_Tenant_Party_Active]
    ON [payroll].[Employments]([TenantId],[PartyId]) WHERE [IsActive] = 1;
GO
CREATE INDEX [IX_PayrollEmployments_Business_Active]
    ON [payroll].[Employments]([BusinessId],[IsActive]) INCLUDE ([PartyId],[MonthlySalary]);
GO

CREATE TABLE [payroll].[DeductionAgreements]
(
    [DeductionAgreementId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [EmploymentId] UNIQUEIDENTIFIER NOT NULL,
    [ConceptId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorityOptionId] UNIQUEIDENTIFIER NOT NULL,
    [BeneficiaryPartyId] UNIQUEIDENTIFIER NULL,
    [ReferenceNumber] NVARCHAR(100) NOT NULL,
    [EvidenceUrl] NVARCHAR(1000) NOT NULL,
    [EffectiveFrom] DATE NOT NULL,
    [EffectiveTo] DATE NULL,
    [AuthorizedTotal] DECIMAL(19,4) NULL,
    [InstallmentAmount] DECIMAL(19,4) NULL,
    [DeductedToDate] DECIMAL(19,4) NOT NULL,
    [Priority] SMALLINT NOT NULL,
    [MustProtectMinimumNetPay] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollDeductionAgreements] PRIMARY KEY ([DeductionAgreementId]),
    CONSTRAINT [FK_PayrollDeductionAgreements_Employment] FOREIGN KEY ([TenantId],[EmploymentId]) REFERENCES [payroll].[Employments]([TenantId],[EmploymentId]),
    CONSTRAINT [FK_PayrollDeductionAgreements_Concept] FOREIGN KEY ([TenantId],[ConceptId]) REFERENCES [payroll].[Concepts]([TenantId],[ConceptId]),
    CONSTRAINT [FK_PayrollDeductionAgreements_Authority] FOREIGN KEY ([AuthorityOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollDeductionAgreements_Beneficiary] FOREIGN KEY ([TenantId],[BeneficiaryPartyId]) REFERENCES [dbo].[Parties]([TenantId],[PartyId]),
    CONSTRAINT [CK_PayrollDeductionAgreements_Range] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]),
    CONSTRAINT [CK_PayrollDeductionAgreements_Amounts] CHECK (([AuthorizedTotal] IS NULL OR [AuthorizedTotal] > 0) AND ([InstallmentAmount] IS NULL OR [InstallmentAmount] > 0) AND [DeductedToDate] >= 0 AND ([AuthorizedTotal] IS NULL OR [DeductedToDate] <= [AuthorizedTotal])),
    CONSTRAINT [CK_PayrollDeductionAgreements_Priority] CHECK ([Priority] BETWEEN 1 AND 999)
);
GO

CREATE TABLE [payroll].[Novelties]
(
    [NoveltyId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [EmploymentId] UNIQUEIDENTIFIER NOT NULL,
    [ConceptId] UNIQUEIDENTIFIER NOT NULL,
    [NoveltyTypeOptionId] UNIQUEIDENTIFIER NOT NULL,
    [ReasonId] UNIQUEIDENTIFIER NULL,
    [DeductionAgreementId] UNIQUEIDENTIFIER NULL,
    [StartDate] DATE NOT NULL,
    [EndDate] DATE NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [UnitAmount] DECIMAL(19,4) NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [Notes] NVARCHAR(500) NULL,
    [EvidenceUrl] NVARCHAR(1000) NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ApprovedBy] UNIQUEIDENTIFIER NULL,
    [ApprovedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollNovelties] PRIMARY KEY ([NoveltyId]),
    CONSTRAINT [UQ_PayrollNovelties_Tenant_Novelty] UNIQUE ([TenantId],[NoveltyId]),
    CONSTRAINT [FK_PayrollNovelties_Employment] FOREIGN KEY ([TenantId],[EmploymentId]) REFERENCES [payroll].[Employments]([TenantId],[EmploymentId]),
    CONSTRAINT [FK_PayrollNovelties_Concept] FOREIGN KEY ([TenantId],[ConceptId]) REFERENCES [payroll].[Concepts]([TenantId],[ConceptId]),
    CONSTRAINT [FK_PayrollNovelties_Type] FOREIGN KEY ([NoveltyTypeOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollNovelties_Reason] FOREIGN KEY ([BusinessId],[ReasonId]) REFERENCES [dbo].[BusinessReasons]([BusinessId],[ReasonId]),
    CONSTRAINT [FK_PayrollNovelties_Agreement] FOREIGN KEY ([DeductionAgreementId]) REFERENCES [payroll].[DeductionAgreements]([DeductionAgreementId]),
    CONSTRAINT [CK_PayrollNovelties_Dates] CHECK ([EndDate] >= [StartDate]),
    CONSTRAINT [CK_PayrollNovelties_Amounts] CHECK ([Quantity] > 0 AND [TotalAmount] >= 0),
    CONSTRAINT [CK_PayrollNovelties_Status] CHECK ([Status] IN (N'Draft',N'Approved',N'Consumed',N'Voided'))
);
GO
CREATE INDEX [IX_PayrollNovelties_Employment_Status_Dates]
    ON [payroll].[Novelties]([EmploymentId],[Status],[StartDate],[EndDate]);
GO

CREATE TABLE [payroll].[Runs]
(
    [PayrollRunId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [RuleSetId] UNIQUEIDENTIFIER NOT NULL,
    [PayFrequencyOptionId] UNIQUEIDENTIFIER NOT NULL,
    [RunKind] NVARCHAR(24) NOT NULL,
    [OriginalPayrollRunId] UNIQUEIDENTIFIER NULL,
    [PeriodStart] DATE NOT NULL,
    [PeriodEnd] DATE NOT NULL,
    [PaymentDate] DATE NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CalculationVersion] INT NOT NULL,
    [InputHash] BINARY(32) NULL,
    [TotalEarnings] DECIMAL(19,4) NOT NULL,
    [TotalDeductions] DECIMAL(19,4) NOT NULL,
    [TotalEmployerContributions] DECIMAL(19,4) NOT NULL,
    [TotalProvisions] DECIMAL(19,4) NOT NULL,
    [NetPayable] DECIMAL(19,4) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CalculatedAt] DATETIMEOFFSET(7) NULL,
    [ApprovedBy] UNIQUEIDENTIFIER NULL,
    [ApprovedAt] DATETIMEOFFSET(7) NULL,
    [ApprovalIdempotencyKey] NVARCHAR(160) NULL,
    [ApprovalRequestHash] BINARY(32) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollRuns] PRIMARY KEY ([PayrollRunId]),
    CONSTRAINT [UQ_PayrollRuns_Tenant_Run] UNIQUE ([TenantId],[PayrollRunId]),
    CONSTRAINT [FK_PayrollRuns_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PayrollRuns_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PayrollRuns_RuleSets] FOREIGN KEY ([RuleSetId]) REFERENCES [payroll].[RuleSets]([RuleSetId]),
    CONSTRAINT [FK_PayrollRuns_Frequency] FOREIGN KEY ([PayFrequencyOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [FK_PayrollRuns_Original] FOREIGN KEY ([TenantId],[OriginalPayrollRunId]) REFERENCES [payroll].[Runs]([TenantId],[PayrollRunId]),
    CONSTRAINT [CK_PayrollRuns_Dates] CHECK ([PeriodEnd] >= [PeriodStart] AND [PaymentDate] >= [PeriodStart]),
    CONSTRAINT [CK_PayrollRuns_Kind] CHECK ([RunKind] IN (N'Regular',N'Adjustment')),
    CONSTRAINT [CK_PayrollRuns_Adjustment] CHECK (([RunKind] = N'Regular' AND [OriginalPayrollRunId] IS NULL) OR ([RunKind] = N'Adjustment' AND [OriginalPayrollRunId] IS NOT NULL)),
    CONSTRAINT [CK_PayrollRuns_Status] CHECK ([Status] IN (N'Draft',N'Calculated',N'Approved',N'Voided')),
    CONSTRAINT [CK_PayrollRuns_Totals] CHECK ([TotalEarnings] >= 0 AND [TotalDeductions] >= 0 AND [TotalEmployerContributions] >= 0 AND [TotalProvisions] >= 0 AND [NetPayable] >= 0)
);
GO
CREATE UNIQUE INDEX [UX_PayrollRuns_Business_ApprovalKey]
    ON [payroll].[Runs]([BusinessId],[ApprovalIdempotencyKey]) WHERE [ApprovalIdempotencyKey] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_PayrollRuns_Business_Regular_Period_Frequency]
    ON [payroll].[Runs]([BusinessId],[PeriodStart],[PeriodEnd],[PayFrequencyOptionId]) WHERE [RunKind] = N'Regular' AND [Status] <> N'Voided';
GO

CREATE TABLE [payroll].[RunEmployees]
(
    [PayrollRunEmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PayrollRunId] UNIQUEIDENTIFIER NOT NULL,
    [EmploymentId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [EmployeeSnapshotJson] NVARCHAR(MAX) NOT NULL,
    [RuleSnapshotJson] NVARCHAR(MAX) NOT NULL,
    [WorkedDays] DECIMAL(9,4) NOT NULL,
    [Earnings] DECIMAL(19,4) NOT NULL,
    [Deductions] DECIMAL(19,4) NOT NULL,
    [EmployerContributions] DECIMAL(19,4) NOT NULL,
    [Provisions] DECIMAL(19,4) NOT NULL,
    [NetPayable] DECIMAL(19,4) NOT NULL,
    [CalculationHash] BINARY(32) NOT NULL,
    CONSTRAINT [PK_PayrollRunEmployees] PRIMARY KEY ([PayrollRunEmployeeId]),
    CONSTRAINT [UQ_PayrollRunEmployees_Run_Employment] UNIQUE ([PayrollRunId],[EmploymentId]),
    CONSTRAINT [FK_PayrollRunEmployees_Run] FOREIGN KEY ([TenantId],[PayrollRunId]) REFERENCES [payroll].[Runs]([TenantId],[PayrollRunId]),
    CONSTRAINT [FK_PayrollRunEmployees_Employment] FOREIGN KEY ([TenantId],[EmploymentId]) REFERENCES [payroll].[Employments]([TenantId],[EmploymentId]),
    CONSTRAINT [FK_PayrollRunEmployees_Party] FOREIGN KEY ([TenantId],[PartyId]) REFERENCES [dbo].[Parties]([TenantId],[PartyId]),
    CONSTRAINT [CK_PayrollRunEmployees_Totals] CHECK ([WorkedDays] >= 0 AND [Earnings] >= 0 AND [Deductions] >= 0 AND [EmployerContributions] >= 0 AND [Provisions] >= 0 AND [NetPayable] >= 0 AND [NetPayable] = [Earnings] - [Deductions])
);
GO

CREATE TABLE [payroll].[RunLines]
(
    [PayrollRunLineId] UNIQUEIDENTIFIER NOT NULL,
    [PayrollRunEmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [ConceptId] UNIQUEIDENTIFIER NOT NULL,
    [NoveltyId] UNIQUEIDENTIFIER NULL,
    [DeductionAgreementId] UNIQUEIDENTIFIER NULL,
    [LineNumber] INT NOT NULL,
    [NatureCode] NVARCHAR(32) NOT NULL,
    [ConceptCode] NVARCHAR(32) NOT NULL,
    [ConceptName] NVARCHAR(160) NOT NULL,
    [DianConceptCode] NVARCHAR(64) NULL,
    [AccountingCategoryCode] NVARCHAR(64) NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [Rate] DECIMAL(19,8) NULL,
    [BaseAmount] DECIMAL(19,4) NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [IsEmployerCost] BIT NOT NULL,
    [IsSalaryBase] BIT NOT NULL,
    CONSTRAINT [PK_PayrollRunLines] PRIMARY KEY ([PayrollRunLineId]),
    CONSTRAINT [UQ_PayrollRunLines_Employee_Number] UNIQUE ([PayrollRunEmployeeId],[LineNumber]),
    CONSTRAINT [FK_PayrollRunLines_RunEmployee] FOREIGN KEY ([PayrollRunEmployeeId]) REFERENCES [payroll].[RunEmployees]([PayrollRunEmployeeId]),
    CONSTRAINT [FK_PayrollRunLines_Concept] FOREIGN KEY ([ConceptId]) REFERENCES [payroll].[Concepts]([ConceptId]),
    CONSTRAINT [FK_PayrollRunLines_Novelty] FOREIGN KEY ([NoveltyId]) REFERENCES [payroll].[Novelties]([NoveltyId]),
    CONSTRAINT [FK_PayrollRunLines_Agreement] FOREIGN KEY ([DeductionAgreementId]) REFERENCES [payroll].[DeductionAgreements]([DeductionAgreementId]),
    CONSTRAINT [CK_PayrollRunLines_Values] CHECK ([Quantity] > 0 AND [Amount] >= 0 AND ([BaseAmount] IS NULL OR [BaseAmount] >= 0))
);
GO

CREATE TABLE [payroll].[PaymentBatches]
(
    [PaymentBatchId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentDate] DATE NOT NULL,
    [PaymentMethodOptionId] UNIQUEIDENTIFIER NOT NULL,
    [ReferenceNumber] NVARCHAR(100) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ConfirmedBy] UNIQUEIDENTIFIER NULL,
    [ConfirmedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollPaymentBatches] PRIMARY KEY ([PaymentBatchId]),
    CONSTRAINT [FK_PayrollPaymentBatches_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PayrollPaymentBatches_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PayrollPaymentBatches_Method] FOREIGN KEY ([PaymentMethodOptionId]) REFERENCES [payroll].[CatalogOptions]([OptionId]),
    CONSTRAINT [CK_PayrollPaymentBatches_Status] CHECK ([Status] IN (N'Draft',N'Confirmed',N'Voided')),
    CONSTRAINT [CK_PayrollPaymentBatches_Total] CHECK ([TotalAmount] >= 0)
);
GO

CREATE TABLE [payroll].[PaymentLines]
(
    [PaymentLineId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentBatchId] UNIQUEIDENTIFIER NOT NULL,
    [PayrollRunEmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [EmployeePaymentReference] NVARCHAR(100) NULL,
    CONSTRAINT [PK_PayrollPaymentLines] PRIMARY KEY ([PaymentLineId]),
    CONSTRAINT [UQ_PayrollPaymentLines_Batch_Employee] UNIQUE ([PaymentBatchId],[PayrollRunEmployeeId]),
    CONSTRAINT [FK_PayrollPaymentLines_Batch] FOREIGN KEY ([PaymentBatchId]) REFERENCES [payroll].[PaymentBatches]([PaymentBatchId]),
    CONSTRAINT [FK_PayrollPaymentLines_RunEmployee] FOREIGN KEY ([PayrollRunEmployeeId]) REFERENCES [payroll].[RunEmployees]([PayrollRunEmployeeId]),
    CONSTRAINT [CK_PayrollPaymentLines_Amount] CHECK ([Amount] > 0)
);
GO
CREATE UNIQUE INDEX [UX_PayrollPaymentLines_RunEmployee]
    ON [payroll].[PaymentLines]([PayrollRunEmployeeId]);
GO

CREATE TABLE [payroll].[ElectronicConfigurations]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [FiscalIssuerConfigurationId] UNIQUEIDENTIFIER NOT NULL,
    [SoftwareIdentificationCode] NVARCHAR(64) NOT NULL,
    [SoftwarePinSecretReference] NVARCHAR(512) NOT NULL,
    [TestSetId] UNIQUEIDENTIFIER NULL,
    [Prefix] NVARCHAR(10) NOT NULL,
    [NextConsecutive] BIGINT NOT NULL,
    [QrValidationUrl] NVARCHAR(512) NOT NULL,
    [IsActive] BIT NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollElectronicConfigurations] PRIMARY KEY ([BusinessId]),
    CONSTRAINT [FK_PayrollElectronicConfigurations_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PayrollElectronicConfigurations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PayrollElectronicConfigurations_Issuer] FOREIGN KEY ([FiscalIssuerConfigurationId]) REFERENCES [dbo].[FiscalIssuerConfigurations]([FiscalIssuerConfigurationId]),
    CONSTRAINT [CK_PayrollElectronicConfigurations_Software] CHECK (
        LEN(LTRIM(RTRIM([SoftwareIdentificationCode]))) BETWEEN 1 AND 64 AND
        LEN(LTRIM(RTRIM([SoftwarePinSecretReference]))) BETWEEN 1 AND 512),
    CONSTRAINT [CK_PayrollElectronicConfigurations_Prefix] CHECK (LEN(LTRIM(RTRIM([Prefix]))) BETWEEN 1 AND 10),
    CONSTRAINT [CK_PayrollElectronicConfigurations_Consecutive] CHECK ([NextConsecutive] > 0),
    CONSTRAINT [CK_PayrollElectronicConfigurations_QrUrl] CHECK ([QrValidationUrl] LIKE N'https://%')
);
GO

CREATE TABLE [payroll].[ElectronicPeriods]
(
    [ElectronicPeriodId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Year] SMALLINT NOT NULL,
    [Month] TINYINT NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ClosedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollElectronicPeriods] PRIMARY KEY ([ElectronicPeriodId]),
    CONSTRAINT [UQ_PayrollElectronicPeriods_Id_Tenant_Business] UNIQUE ([ElectronicPeriodId],[TenantId],[BusinessId]),
    CONSTRAINT [UQ_PayrollElectronicPeriods_Tenant_Business_Period] UNIQUE ([TenantId],[BusinessId],[Year],[Month]),
    CONSTRAINT [FK_PayrollElectronicPeriods_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PayrollElectronicPeriods_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_PayrollElectronicPeriods_Values] CHECK ([Year] BETWEEN 2020 AND 9999 AND [Month] BETWEEN 1 AND 12),
    CONSTRAINT [CK_PayrollElectronicPeriods_Status] CHECK ([Status] IN (N'Draft',N'Generated',N'Submitted',N'Closed'))
);
GO

CREATE TABLE [payroll].[ElectronicDocuments]
(
    [ElectronicPayrollDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [ElectronicPeriodId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentKind] NVARCHAR(24) NOT NULL,
    [OriginalDocumentId] UNIQUEIDENTIFIER NULL,
    [FiscalDocumentId] UNIQUEIDENTIFIER NULL,
    [TestSetId] UNIQUEIDENTIFIER NULL,
    [SourceSnapshotJson] NVARCHAR(MAX) NOT NULL,
    [SourceHash] BINARY(32) NOT NULL,
    [Status] NVARCHAR(32) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollElectronicDocuments] PRIMARY KEY ([ElectronicPayrollDocumentId]),
    CONSTRAINT [UQ_PayrollElectronicDocuments_Period_Party_Kind] UNIQUE ([ElectronicPeriodId],[PartyId],[DocumentKind]),
    CONSTRAINT [FK_PayrollElectronicDocuments_Period] FOREIGN KEY ([ElectronicPeriodId],[TenantId],[BusinessId]) REFERENCES [payroll].[ElectronicPeriods]([ElectronicPeriodId],[TenantId],[BusinessId]),
    CONSTRAINT [FK_PayrollElectronicDocuments_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PayrollElectronicDocuments_Party] FOREIGN KEY ([TenantId],[PartyId]) REFERENCES [dbo].[Parties]([TenantId],[PartyId]),
    CONSTRAINT [FK_PayrollElectronicDocuments_Original] FOREIGN KEY ([OriginalDocumentId]) REFERENCES [payroll].[ElectronicDocuments]([ElectronicPayrollDocumentId]),
    CONSTRAINT [FK_PayrollElectronicDocuments_Fiscal] FOREIGN KEY ([FiscalDocumentId]) REFERENCES [dbo].[FiscalDocuments]([DocumentId]),
    CONSTRAINT [CK_PayrollElectronicDocuments_Kind] CHECK ([DocumentKind] IN (N'Individual',N'Replace',N'Delete')),
    CONSTRAINT [CK_PayrollElectronicDocuments_Status] CHECK ([Status] IN (N'Draft',N'Queued',N'Accepted',N'Rejected',N'Failed'))
);
GO

CREATE TABLE [payroll].[OutboxMessages]
(
    [OutboxMessageId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AggregateId] UNIQUEIDENTIFIER NOT NULL,
    [MessageType] NVARCHAR(64) NOT NULL,
    [PayloadJson] NVARCHAR(MAX) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [PublishedAt] DATETIMEOFFSET(7) NULL,
    [AttemptCount] INT NOT NULL,
    [LastError] NVARCHAR(1000) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollOutboxMessages] PRIMARY KEY ([OutboxMessageId]),
    CONSTRAINT [UQ_PayrollOutboxMessages_Aggregate_Type] UNIQUE ([AggregateId],[MessageType]),
    CONSTRAINT [FK_PayrollOutboxMessages_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PayrollOutboxMessages_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [CK_PayrollOutboxMessages_Attempts] CHECK ([AttemptCount] >= 0)
);
GO
CREATE INDEX [IX_PayrollOutboxMessages_Pending]
    ON [payroll].[OutboxMessages]([PublishedAt],[OccurredAt]) INCLUDE ([TenantId],[BusinessId],[AggregateId],[MessageType]);
GO
