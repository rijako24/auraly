CREATE TABLE [reference].[PaymentMethods]
(
    [TenantId] uniqueidentifier NOT NULL,
    [PaymentMethodId] uniqueidentifier NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [PaymentTypeId] uniqueidentifier NOT NULL,
    [AccountId] uniqueidentifier NOT NULL,
    [UseForSales] bit NOT NULL,
    [UseForReceipts] bit NOT NULL,
    [UseForSupplierPayments] bit NOT NULL,
    [UseForPayroll] bit NOT NULL,
    [UseForVouchers] bit NOT NULL CONSTRAINT [DF_PaymentMethods_UseForVouchers] DEFAULT (0),
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [Version] int NOT NULL CONSTRAINT [DF_PaymentMethods_Version] DEFAULT 1,
    [CreatedAt] datetimeoffset(7) NOT NULL,
    [UpdatedAt] datetimeoffset(7) NOT NULL,
    [TypeLockedAt] datetimeoffset(7) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_PaymentMethods] PRIMARY KEY ([TenantId],[PaymentMethodId]),
    CONSTRAINT [UQ_PaymentMethods_Identity] UNIQUE ([PaymentMethodId]),
    CONSTRAINT [UQ_PaymentMethods_Code] UNIQUE ([TenantId],[Code]),
    CONSTRAINT [FK_PaymentMethods_Tenant] FOREIGN KEY ([TenantId]) REFERENCES dbo.Tenants(TenantId),
    CONSTRAINT [FK_PaymentMethods_Type] FOREIGN KEY ([PaymentTypeId]) REFERENCES reference.Options(OptionId),
    CONSTRAINT [FK_PaymentMethods_Account] FOREIGN KEY ([TenantId],[AccountId]) REFERENCES dbo.AccountingAccounts(TenantId,AccountId),
    CONSTRAINT [CK_PaymentMethods_Values] CHECK (LEN(LTRIM(RTRIM(Code)))>0 AND LEN(LTRIM(RTRIM(Name)))>0 AND SortOrder>=0 AND Version>0)
);
GO
CREATE INDEX IX_PaymentMethods_Active ON reference.PaymentMethods(TenantId,IsActive,SortOrder,PaymentMethodId)
    INCLUDE(Code,Name,PaymentTypeId,AccountId,UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,UseForVouchers);
GO
