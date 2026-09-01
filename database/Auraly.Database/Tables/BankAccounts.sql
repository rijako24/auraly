CREATE TABLE [accounting].[BankAccounts]
(
    [BankAccountId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [AccountingAccountId] UNIQUEIDENTIFIER NOT NULL,
    [AccountTypeOptionId] UNIQUEIDENTIFIER NOT NULL,
    [BankName] NVARCHAR(120) NOT NULL,
    [AccountNumber] NVARCHAR(64) NOT NULL,
    [DisplayName] NVARCHAR(160) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL CONSTRAINT [DF_BankAccounts_Currency] DEFAULT(N'COP'),
    [IsPrimary] BIT NOT NULL CONSTRAINT [DF_BankAccounts_IsPrimary] DEFAULT(0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_BankAccounts_IsActive] DEFAULT(1),
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_BankAccounts] PRIMARY KEY CLUSTERED ([BankAccountId]),
    CONSTRAINT [FK_BankAccounts_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_BankAccounts_AccountingAccount] FOREIGN KEY ([TenantId],[AccountingAccountId])
      REFERENCES [dbo].[AccountingAccounts]([TenantId],[AccountId]),
    CONSTRAINT [FK_BankAccounts_AccountType] FOREIGN KEY ([AccountTypeOptionId]) REFERENCES [reference].[Options]([OptionId]),
    CONSTRAINT [FK_BankAccounts_CreatedBy] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_BankAccounts_UpdatedBy] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_BankAccounts_Text] CHECK
      (LEN(LTRIM(RTRIM([BankName])))>0 AND LEN(LTRIM(RTRIM([AccountNumber])))>0
       AND LEN(LTRIM(RTRIM([DisplayName])))>0),
    CONSTRAINT [CK_BankAccounts_Currency] CHECK ([CurrencyCode]=N'COP')
);
GO
CREATE UNIQUE INDEX [UX_BankAccounts_Tenant_Bank_Number]
  ON [accounting].[BankAccounts]([TenantId],[BankName],[AccountNumber]);
GO
CREATE UNIQUE INDEX [UX_BankAccounts_Tenant_Primary]
  ON [accounting].[BankAccounts]([TenantId]) WHERE [IsPrimary]=1 AND [IsActive]=1;
GO
CREATE INDEX [IX_BankAccounts_Tenant_Active]
  ON [accounting].[BankAccounts]([TenantId],[IsActive],[DisplayName])
  INCLUDE([AccountingAccountId],[AccountTypeOptionId],[BankName],[AccountNumber],[CurrencyCode],[IsPrimary]);
GO
