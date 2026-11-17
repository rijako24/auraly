CREATE TABLE payroll.AccountingProfiles
(
    ProfileId UNIQUEIDENTIFIER NOT NULL,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    Code NVARCHAR(32) NOT NULL,
    Name NVARCHAR(160) NOT NULL,
    RuleSetId UNIQUEIDENTIFIER NOT NULL,
    IsActive BIT NOT NULL,
    UpdatedBy UNIQUEIDENTIFIER NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT PK_PayrollAccountingProfiles PRIMARY KEY (ProfileId),
    CONSTRAINT UQ_PayrollAccountingProfiles_Tenant_Id UNIQUE (TenantId,ProfileId),
    CONSTRAINT UQ_PayrollAccountingProfiles_Tenant_Code UNIQUE (TenantId,Code),
    CONSTRAINT FK_PayrollAccountingProfiles_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(TenantId),
    CONSTRAINT FK_PayrollAccountingProfiles_Rules FOREIGN KEY (RuleSetId) REFERENCES payroll.RuleSets(RuleSetId)
);
GO
CREATE INDEX IX_PayrollAccountingProfiles_Tenant_Name ON payroll.AccountingProfiles(TenantId,Name,ProfileId) INCLUDE(IsActive);
GO
CREATE TABLE payroll.ProfileAccountMappings
(
    MappingId UNIQUEIDENTIFIER NOT NULL,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    ProfileId UNIQUEIDENTIFIER NOT NULL,
    -- NULL identifies net payroll payable, rather than a fictitious concept.
    ConceptId UNIQUEIDENTIFIER NULL,
    DebitAccountId UNIQUEIDENTIFIER NULL,
    CreditAccountId UNIQUEIDENTIFIER NULL,
    BeneficiaryPartyId UNIQUEIDENTIFIER NULL,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    CONSTRAINT PK_PayrollProfileAccountMappings PRIMARY KEY (MappingId),
    CONSTRAINT FK_PayrollProfileMappings_Profile FOREIGN KEY (TenantId,ProfileId) REFERENCES payroll.AccountingProfiles(TenantId,ProfileId),
    CONSTRAINT FK_PayrollProfileMappings_Concept FOREIGN KEY (TenantId,ConceptId) REFERENCES payroll.Concepts(TenantId,ConceptId),
    CONSTRAINT FK_PayrollProfileMappings_Debit FOREIGN KEY (TenantId,DebitAccountId) REFERENCES dbo.AccountingAccounts(TenantId,AccountId),
    CONSTRAINT FK_PayrollProfileMappings_Credit FOREIGN KEY (TenantId,CreditAccountId) REFERENCES dbo.AccountingAccounts(TenantId,AccountId),
    CONSTRAINT FK_PayrollProfileMappings_Beneficiary FOREIGN KEY (TenantId,BeneficiaryPartyId) REFERENCES dbo.Parties(TenantId,PartyId),
    CONSTRAINT CK_PayrollProfileMappings_Accounts CHECK (DebitAccountId IS NOT NULL OR CreditAccountId IS NOT NULL OR BeneficiaryPartyId IS NOT NULL),
    CONSTRAINT CK_PayrollProfileMappings_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo>=EffectiveFrom),
    CONSTRAINT CK_PayrollProfileMappings_Net CHECK (ConceptId IS NOT NULL OR (DebitAccountId IS NULL AND BeneficiaryPartyId IS NULL))
);
GO
CREATE INDEX IX_PayrollProfileMappings_Profile_Concept_Date
 ON payroll.ProfileAccountMappings(ProfileId,ConceptId,EffectiveFrom) INCLUDE(EffectiveTo,DebitAccountId,CreditAccountId,TenantId);
GO
