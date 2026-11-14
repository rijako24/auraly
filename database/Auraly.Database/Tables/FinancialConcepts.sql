CREATE TABLE reference.FinancialConcepts
(
    FinancialConceptId uniqueidentifier NOT NULL PRIMARY KEY,
    TenantId uniqueidentifier NOT NULL REFERENCES dbo.Tenants(TenantId),
    Code nvarchar(64) NOT NULL,
    Name nvarchar(160) NOT NULL,
    AccountId uniqueidentifier NOT NULL REFERENCES dbo.AccountingAccounts(AccountId),
    SideOptionId uniqueidentifier NOT NULL REFERENCES reference.Options(OptionId),
    UseForReceipts bit NOT NULL,
    UseForSupplierPayments bit NOT NULL,
    UseForVouchers bit NOT NULL,
    IsActive bit NOT NULL,
    SortOrder int NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT UQ_FinancialConcepts_Tenant_Code UNIQUE(TenantId,Code),
    CONSTRAINT CK_FinancialConcepts_Code CHECK(LEN(LTRIM(RTRIM(Code)))>0),
    CONSTRAINT CK_FinancialConcepts_Name CHECK(LEN(LTRIM(RTRIM(Name)))>0),
    CONSTRAINT CK_FinancialConcepts_Order CHECK(SortOrder BETWEEN 0 AND 9999)
);
GO
CREATE INDEX IX_FinancialConcepts_Tenant_Order ON reference.FinancialConcepts(TenantId,SortOrder,Code,FinancialConceptId)
    INCLUDE(Name,AccountId,SideOptionId,IsActive,UseForReceipts,UseForSupplierPayments,UseForVouchers);
GO

-- Membership only: the accepted source owns the amount, account and evidence.
-- AccountingSourcesAccept writes this index in its existing transaction.
CREATE TABLE accounting.SourceConceptIndex
(
    FinancialConceptId uniqueidentifier NOT NULL REFERENCES reference.FinancialConcepts(FinancialConceptId),
    SourceDocumentId uniqueidentifier NOT NULL,
    SourceDocumentType nvarchar(64) NOT NULL,
    CONSTRAINT PK_SourceConceptIndex PRIMARY KEY(FinancialConceptId,SourceDocumentId,SourceDocumentType),
    CONSTRAINT FK_SourceConceptIndex_Source FOREIGN KEY(SourceDocumentId,SourceDocumentType)
      REFERENCES dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType)
);
GO
