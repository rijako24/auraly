IF COL_LENGTH(N'dbo.CustomerPayments', N'BankAccountId') IS NULL
    ALTER TABLE dbo.CustomerPayments ADD BankAccountId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH(N'dbo.SupplierPayments', N'BankAccountId') IS NULL
    ALTER TABLE dbo.SupplierPayments ADD BankAccountId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_CustomerPayments_BankAccounts')
    ALTER TABLE dbo.CustomerPayments WITH CHECK ADD CONSTRAINT FK_CustomerPayments_BankAccounts
      FOREIGN KEY(BankAccountId) REFERENCES accounting.BankAccounts(BankAccountId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SupplierPayments_BankAccounts')
    ALTER TABLE dbo.SupplierPayments WITH CHECK ADD CONSTRAINT FK_SupplierPayments_BankAccounts
      FOREIGN KEY(BankAccountId) REFERENCES accounting.BankAccounts(BankAccountId);
GO
