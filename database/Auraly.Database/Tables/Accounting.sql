CREATE TABLE [dbo].[AccountingConfigurationProfiles]
(
    [ProfileCode] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsDefault] BIT NOT NULL CONSTRAINT [DF_AccountingConfigurationProfiles_IsDefault] DEFAULT (0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_AccountingConfigurationProfiles_IsActive] DEFAULT (1),
    CONSTRAINT [PK_AccountingConfigurationProfiles] PRIMARY KEY CLUSTERED ([ProfileCode])
);
GO
CREATE UNIQUE INDEX [UX_AccountingConfigurationProfiles_Default]
    ON [dbo].[AccountingConfigurationProfiles]([IsDefault]) WHERE [IsDefault]=1 AND [IsActive]=1;
GO

CREATE TABLE [dbo].[AccountingTenantSettings]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [FunctionalCurrencyCode] CHAR(3) NOT NULL CONSTRAINT [DF_AccountingTenantSettings_Currency] DEFAULT N'COP',
    [EffectiveFrom] DATE NULL,
    [OpeningBalanceMode] NVARCHAR(24) NULL,
    [ActivatedAt] DATETIMEOFFSET(7) NULL,
    [ActivatedByUserId] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingTenantSettings] PRIMARY KEY CLUSTERED ([TenantId]),
    CONSTRAINT [FK_AccountingTenantSettings_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_AccountingTenantSettings_ActivatedBy] FOREIGN KEY ([ActivatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_AccountingTenantSettings_Status] CHECK ([Status] IN (N'Disabled',N'Configuring',N'Ready')),
    CONSTRAINT [CK_AccountingTenantSettings_Opening] CHECK ([OpeningBalanceMode] IS NULL OR [OpeningBalanceMode] IN (N'ZeroDeclared',N'ImportedAndApproved')),
    CONSTRAINT [CK_AccountingTenantSettings_Ready] CHECK
      (([Status]<>N'Ready' AND [EffectiveFrom] IS NULL AND [ActivatedAt] IS NULL AND [ActivatedByUserId] IS NULL)
       OR
       ([Status]=N'Ready' AND [EffectiveFrom] IS NOT NULL AND [OpeningBalanceMode] IS NOT NULL
        AND [ActivatedAt] IS NOT NULL AND [ActivatedByUserId] IS NOT NULL))
);
GO

CREATE TABLE [dbo].[AccountingConfigurationProfileAccounts]
(
    [ProfileCode] NVARCHAR(32) NOT NULL,
    [Category] NVARCHAR(64) NOT NULL,
    [DisplayName] NVARCHAR(160) NOT NULL,
    [AccountCode] NVARCHAR(32) NOT NULL,
    [AccountName] NVARCHAR(200) NOT NULL,
    [AccountType] NVARCHAR(24) NOT NULL,
    [AllowsPosting] BIT NOT NULL CONSTRAINT [DF_AccountingConfigurationProfileAccounts_AllowsPosting] DEFAULT (1),
    [RequiresParty] BIT NOT NULL CONSTRAINT [DF_AccountingConfigurationProfileAccounts_RequiresParty] DEFAULT (0),
    [IsRequired] BIT NOT NULL CONSTRAINT [DF_AccountingConfigurationProfileAccounts_IsRequired] DEFAULT (1),
    [DisplayOrder] INT NOT NULL,
    CONSTRAINT [PK_AccountingConfigurationProfileAccounts] PRIMARY KEY CLUSTERED ([ProfileCode],[Category]),
    CONSTRAINT [FK_AccountingConfigurationProfileAccounts_Profile] FOREIGN KEY ([ProfileCode]) REFERENCES [dbo].[AccountingConfigurationProfiles]([ProfileCode]),
    CONSTRAINT [CK_AccountingConfigurationProfileAccounts_Type] CHECK ([AccountType] IN (N'Asset',N'Liability',N'Equity',N'Revenue',N'Expense',N'ContraRevenue')),
    CONSTRAINT [CK_AccountingConfigurationProfileAccounts_Order] CHECK ([DisplayOrder]>0)
);
GO

CREATE TABLE [dbo].[AccountingSourceCategoryMappings]
(
    [ProfileCode] NVARCHAR(32) NOT NULL,
    [SourceType] NVARCHAR(40) NOT NULL,
    [SourceCode] NVARCHAR(64) NOT NULL,
    [Category] NVARCHAR(64) NOT NULL,
    CONSTRAINT [PK_AccountingSourceCategoryMappings] PRIMARY KEY CLUSTERED ([ProfileCode],[SourceType],[SourceCode]),
    CONSTRAINT [FK_AccountingSourceCategoryMappings_Profile] FOREIGN KEY ([ProfileCode]) REFERENCES [dbo].[AccountingConfigurationProfiles]([ProfileCode]),
    CONSTRAINT [FK_AccountingSourceCategoryMappings_Category] FOREIGN KEY ([ProfileCode],[Category]) REFERENCES [dbo].[AccountingConfigurationProfileAccounts]([ProfileCode],[Category])
);
GO

CREATE TABLE [dbo].[AccountingConfigurationProfileExpenseConcepts]
(
    [ProfileCode] NVARCHAR(32) NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [ExpenseAccountCategory] NVARCHAR(64) NOT NULL,
    [DisplayOrder] INT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_AccountingConfigurationProfileExpenseConcepts_IsActive] DEFAULT (1),
    CONSTRAINT [PK_AccountingConfigurationProfileExpenseConcepts] PRIMARY KEY CLUSTERED ([ProfileCode],[Code]),
    CONSTRAINT [FK_AccountingConfigurationProfileExpenseConcepts_Profile] FOREIGN KEY ([ProfileCode]) REFERENCES [dbo].[AccountingConfigurationProfiles]([ProfileCode]),
    CONSTRAINT [FK_AccountingConfigurationProfileExpenseConcepts_Account] FOREIGN KEY ([ProfileCode],[ExpenseAccountCategory]) REFERENCES [dbo].[AccountingConfigurationProfileAccounts]([ProfileCode],[Category]),
    CONSTRAINT [CK_AccountingConfigurationProfileExpenseConcepts_Order] CHECK ([DisplayOrder]>0)
);
GO

CREATE TABLE [dbo].[ReasonTemplates]
(
    [ProfileCode] NVARCHAR(32) NOT NULL,
    [ReasonType] NVARCHAR(64) NOT NULL,
    [Code] NVARCHAR(40) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [Direction] NVARCHAR(8) NULL,
    [CounterpartAccountingCategory] NVARCHAR(64) NULL,
    [RequiresReference] BIT NOT NULL CONSTRAINT [DF_ReasonTemplates_RequiresReference] DEFAULT (0),
    [DisplayOrder] INT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ReasonTemplates_IsActive] DEFAULT (1),
    CONSTRAINT [PK_ReasonTemplates] PRIMARY KEY CLUSTERED ([ProfileCode],[ReasonType],[Code]),
    CONSTRAINT [FK_ReasonTemplates_Profile] FOREIGN KEY ([ProfileCode]) REFERENCES [dbo].[AccountingConfigurationProfiles]([ProfileCode]),
    CONSTRAINT [CK_ReasonTemplates_Direction] CHECK ([Direction] IS NULL OR [Direction] IN (N'In',N'Out')),
    CONSTRAINT [CK_ReasonTemplates_Order] CHECK ([DisplayOrder]>0)
);
GO

CREATE TABLE [dbo].[AccountingAccounts]
(
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [AccountType] NVARCHAR(24) NOT NULL,
    [AllowsPosting] BIT NOT NULL,
    [RequiresParty] BIT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_AccountingAccounts_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingAccounts] PRIMARY KEY CLUSTERED ([AccountId]),
    CONSTRAINT [UQ_AccountingAccounts_Tenant_Account] UNIQUE ([TenantId],[AccountId]),
    CONSTRAINT [UQ_AccountingAccounts_Tenant_Code] UNIQUE ([TenantId],[Code]),
    CONSTRAINT [FK_AccountingAccounts_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [CK_AccountingAccounts_Type] CHECK ([AccountType] IN (N'Asset',N'Liability',N'Equity',N'Revenue',N'Expense',N'ContraRevenue'))
);
GO

CREATE TABLE [dbo].[AccountingCostCenters]
(
    [CostCenterId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [ParentCostCenterId] UNIQUEIDENTIFIER NULL,
    [IsDefault] BIT NOT NULL CONSTRAINT [DF_AccountingCostCenters_IsDefault] DEFAULT (0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_AccountingCostCenters_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingCostCenters] PRIMARY KEY CLUSTERED ([CostCenterId]),
    CONSTRAINT [UQ_AccountingCostCenters_Business_Center] UNIQUE ([BusinessId],[CostCenterId]),
    CONSTRAINT [UQ_AccountingCostCenters_Business_Code] UNIQUE ([BusinessId],[Code]),
    CONSTRAINT [FK_AccountingCostCenters_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_AccountingCostCenters_Parent] FOREIGN KEY ([BusinessId],[ParentCostCenterId]) REFERENCES [dbo].[AccountingCostCenters]([BusinessId],[CostCenterId])
);
GO
CREATE UNIQUE INDEX [UX_AccountingCostCenters_Business_Default]
    ON [dbo].[AccountingCostCenters]([BusinessId]) WHERE [IsDefault]=1 AND [IsActive]=1;
GO

CREATE TABLE [dbo].[BusinessReasons]
(
    [ReasonId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ReasonType] NVARCHAR(64) NOT NULL,
    [Code] NVARCHAR(40) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [Direction] NVARCHAR(8) NULL,
    [CounterpartAccountingCategory] NVARCHAR(64) NULL,
    [DefaultCostCenterId] UNIQUEIDENTIFIER NULL,
    [RequiresReference] BIT NOT NULL CONSTRAINT [DF_BusinessReasons_RequiresReference] DEFAULT (0),
    [IsSystem] BIT NOT NULL CONSTRAINT [DF_BusinessReasons_IsSystem] DEFAULT (0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_BusinessReasons_IsActive] DEFAULT (1),
    [DisplayOrder] INT NOT NULL CONSTRAINT [DF_BusinessReasons_DisplayOrder] DEFAULT (0),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_BusinessReasons] PRIMARY KEY CLUSTERED ([ReasonId]),
    CONSTRAINT [UQ_BusinessReasons_Business_Reason] UNIQUE ([BusinessId],[ReasonId]),
    CONSTRAINT [UQ_BusinessReasons_Business_Type_Code] UNIQUE ([BusinessId],[ReasonType],[Code]),
    CONSTRAINT [FK_BusinessReasons_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_BusinessReasons_CostCenters] FOREIGN KEY ([DefaultCostCenterId]) REFERENCES [dbo].[AccountingCostCenters]([CostCenterId]),
    CONSTRAINT [CK_BusinessReasons_Direction] CHECK ([Direction] IS NULL OR [Direction] IN (N'In',N'Out')),
    CONSTRAINT [CK_BusinessReasons_Order] CHECK ([DisplayOrder] BETWEEN 0 AND 9999)
);
GO
CREATE INDEX [IX_BusinessReasons_Business_Type_Active]
    ON [dbo].[BusinessReasons]([BusinessId],[ReasonType],[IsActive],[DisplayOrder])
    INCLUDE([Code],[Name],[Direction],[CounterpartAccountingCategory],[DefaultCostCenterId],[RequiresReference]);
GO

CREATE TABLE [dbo].[AccountingPeriods]
(
    [PeriodId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(80) NOT NULL,
    [StartsOn] DATE NOT NULL,
    [EndsOn] DATE NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ClosedAt] DATETIMEOFFSET(7) NULL,
    [ClosedByUserId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingPeriods] PRIMARY KEY CLUSTERED ([PeriodId]),
    CONSTRAINT [UQ_AccountingPeriods_Tenant_Period] UNIQUE ([TenantId],[PeriodId]),
    CONSTRAINT [UQ_AccountingPeriods_Tenant_Range] UNIQUE ([TenantId],[StartsOn],[EndsOn]),
    CONSTRAINT [FK_AccountingPeriods_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_AccountingPeriods_ClosedBy] FOREIGN KEY ([ClosedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_AccountingPeriods_Range] CHECK ([EndsOn]>=[StartsOn]),
    CONSTRAINT [CK_AccountingPeriods_Status] CHECK ([Status] IN (N'Open',N'Closed'))
);
GO
CREATE INDEX [IX_AccountingPeriods_Tenant_Status_Dates]
    ON [dbo].[AccountingPeriods]([TenantId],[Status],[StartsOn],[EndsOn]);
GO

CREATE TABLE [dbo].[AccountingAccountMappings]
(
    [MappingId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NULL,
    [Category] NVARCHAR(64) NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [EffectiveFrom] DATE NOT NULL,
    [EffectiveTo] DATE NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingAccountMappings] PRIMARY KEY CLUSTERED ([MappingId]),
    CONSTRAINT [FK_AccountingAccountMappings_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_AccountingAccountMappings_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_AccountingAccountMappings_Accounts] FOREIGN KEY ([TenantId],[AccountId]) REFERENCES [dbo].[AccountingAccounts]([TenantId],[AccountId]),
    CONSTRAINT [CK_AccountingAccountMappings_Range] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo]>=[EffectiveFrom])
);
GO
CREATE UNIQUE INDEX [UX_AccountingAccountMappings_Tenant_Default]
    ON [dbo].[AccountingAccountMappings]([TenantId],[Category],[EffectiveFrom]) WHERE [BusinessId] IS NULL;
GO
CREATE UNIQUE INDEX [UX_AccountingAccountMappings_Business]
    ON [dbo].[AccountingAccountMappings]([BusinessId],[Category],[EffectiveFrom]) WHERE [BusinessId] IS NOT NULL;
GO

CREATE TABLE [dbo].[AccountingVoucherCursors]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [LastAssignedNumber] BIGINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingVoucherCursors] PRIMARY KEY CLUSTERED ([TenantId]),
    CONSTRAINT [FK_AccountingVoucherCursors_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [CK_AccountingVoucherCursors_Value] CHECK ([LastAssignedNumber]>=0)
);
GO

CREATE TABLE [dbo].[AccountingPostingJobs]
(
    [AccountingPostingJobId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [SourcePayloadHash] BINARY(32) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(40) NOT NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_AccountingPostingJobs_Attempts] DEFAULT (0),
    [LastErrorCode] NVARCHAR(80) NULL,
    [LastErrorMessage] NVARCHAR(1000) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [LastAttemptAt] DATETIMEOFFSET(7) NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingPostingJobs] PRIMARY KEY CLUSTERED ([AccountingPostingJobId]),
    CONSTRAINT [UQ_AccountingPostingJobs_Source] UNIQUE ([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [FK_AccountingPostingJobs_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_AccountingPostingJobs_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_AccountingPostingJobs_Source] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType]) REFERENCES [dbo].[DocumentProcessingJobs]([DocumentId],[DocumentType]),
    CONSTRAINT [CK_AccountingPostingJobs_Status] CHECK ([Status] IN (N'Pending',N'AccountingPendingConfiguration',N'Posted')),
    CONSTRAINT [CK_AccountingPostingJobs_Attempts] CHECK ([AttemptCount]>=0)
);
GO
CREATE INDEX [IX_AccountingPostingJobs_Tenant_Status_Date]
    ON [dbo].[AccountingPostingJobs]([TenantId],[Status],[OccurredAt]) INCLUDE([BusinessId],[SourceDocumentId],[SourceDocumentType]);
GO

CREATE TABLE [dbo].[AccountingEntries]
(
    [EntryId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PeriodId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [EntryNumber] NVARCHAR(24) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [PostedAt] DATETIMEOFFSET(7) NOT NULL,
    [Description] NVARCHAR(300) NOT NULL,
    [DebitTotal] DECIMAL(19,4) NOT NULL,
    [CreditTotal] DECIMAL(19,4) NOT NULL,
    [SourcePayloadHash] BINARY(32) NOT NULL,
    [RuleVersion] SMALLINT NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_AccountingEntries] PRIMARY KEY CLUSTERED ([EntryId]),
    CONSTRAINT [UQ_AccountingEntries_Source] UNIQUE ([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [UQ_AccountingEntries_Tenant_Number] UNIQUE ([TenantId],[EntryNumber]),
    CONSTRAINT [FK_AccountingEntries_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_AccountingEntries_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_AccountingEntries_Periods] FOREIGN KEY ([TenantId],[PeriodId]) REFERENCES [dbo].[AccountingPeriods]([TenantId],[PeriodId]),
    CONSTRAINT [FK_AccountingEntries_PostingJob] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType]) REFERENCES [dbo].[AccountingPostingJobs]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [CK_AccountingEntries_Balanced] CHECK ([DebitTotal]>0 AND [DebitTotal]=[CreditTotal]),
    CONSTRAINT [CK_AccountingEntries_RuleVersion] CHECK ([RuleVersion]>0)
);
GO
CREATE INDEX [IX_AccountingEntries_Tenant_Date]
    ON [dbo].[AccountingEntries]([TenantId],[OccurredAt],[EntryId]) INCLUDE([BusinessId],[EntryNumber],[DebitTotal],[CreditTotal]);
GO

CREATE TABLE [dbo].[AccountingEntryLines]
(
    [EntryId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [CostCenterId] UNIQUEIDENTIFIER NULL,
    [Description] NVARCHAR(300) NOT NULL,
    [Debit] DECIMAL(19,4) NOT NULL,
    [Credit] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_AccountingEntryLines] PRIMARY KEY CLUSTERED ([EntryId],[LineNumber]),
    CONSTRAINT [FK_AccountingEntryLines_Entry] FOREIGN KEY ([EntryId]) REFERENCES [dbo].[AccountingEntries]([EntryId]),
    CONSTRAINT [FK_AccountingEntryLines_Account] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    CONSTRAINT [FK_AccountingEntryLines_Party] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties]([PartyId]),
    CONSTRAINT [FK_AccountingEntryLines_CostCenter] FOREIGN KEY ([CostCenterId]) REFERENCES [dbo].[AccountingCostCenters]([CostCenterId]),
    CONSTRAINT [CK_AccountingEntryLines_Number] CHECK ([LineNumber]>0),
    CONSTRAINT [CK_AccountingEntryLines_Side] CHECK (([Debit]>0 AND [Credit]=0) OR ([Credit]>0 AND [Debit]=0))
);
GO
CREATE INDEX [IX_AccountingEntryLines_Account_Entry]
    ON [dbo].[AccountingEntryLines]([AccountId],[EntryId]) INCLUDE([Debit],[Credit],[PartyId],[CostCenterId]);
GO
