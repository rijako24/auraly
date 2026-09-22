SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM dbo.CustomerPayments WHERE Status = N'Accepted')
    OR EXISTS (SELECT 1 FROM dbo.SupplierPayments WHERE Status = N'Accepted')
    THROW 51620, N'Process accepted portfolio payments before changing the payment contract.', 1;

IF COL_LENGTH(N'dbo.CustomerPayments', N'PaymentBreakdownJson') IS NULL
    ALTER TABLE dbo.CustomerPayments ADD PaymentBreakdownJson NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.SupplierPayments', N'PaymentBreakdownJson') IS NULL
    ALTER TABLE dbo.SupplierPayments ADD PaymentBreakdownJson NVARCHAR(MAX) NULL;
GO

IF COL_LENGTH(N'dbo.CustomerPayments', N'PaymentMethod') IS NOT NULL
    EXEC sys.sp_executesql N'
        UPDATE payment
        SET PaymentBreakdownJson = (
            SELECT 1 AS LineNumber, payment.PaymentMethod AS MethodCode,
                   payment.TotalAmount AS Amount, payment.BankAccountId AS BankAccountId,
                   payment.Reference AS Reference
            FOR JSON PATH, INCLUDE_NULL_VALUES)
        FROM dbo.CustomerPayments payment
        WHERE payment.PaymentBreakdownJson IS NULL;';

IF COL_LENGTH(N'dbo.SupplierPayments', N'PaymentMethod') IS NOT NULL
    EXEC sys.sp_executesql N'
        UPDATE payment
        SET PaymentBreakdownJson = (
            SELECT 1 AS LineNumber, payment.PaymentMethod AS MethodCode,
                   payment.TotalAmount AS Amount, payment.BankAccountId AS BankAccountId,
                   payment.Reference AS Reference
            FOR JSON PATH, INCLUDE_NULL_VALUES)
        FROM dbo.SupplierPayments payment
        WHERE payment.PaymentBreakdownJson IS NULL;';

IF EXISTS (SELECT 1 FROM dbo.CustomerPayments
           WHERE PaymentBreakdownJson IS NULL OR ISJSON(PaymentBreakdownJson) <> 1)
    OR EXISTS (SELECT 1 FROM dbo.SupplierPayments
              WHERE PaymentBreakdownJson IS NULL OR ISJSON(PaymentBreakdownJson) <> 1)
    THROW 51621, N'Portfolio payment breakdown migration is incomplete.', 1;

ALTER TABLE dbo.CustomerPayments ALTER COLUMN PaymentBreakdownJson NVARCHAR(MAX) NOT NULL;
ALTER TABLE dbo.SupplierPayments ALTER COLUMN PaymentBreakdownJson NVARCHAR(MAX) NOT NULL;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerPayments_BankAccounts')
    ALTER TABLE dbo.CustomerPayments DROP CONSTRAINT FK_CustomerPayments_BankAccounts;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SupplierPayments_BankAccounts')
    ALTER TABLE dbo.SupplierPayments DROP CONSTRAINT FK_SupplierPayments_BankAccounts;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerPayments_BankAccount')
    ALTER TABLE dbo.CustomerPayments DROP CONSTRAINT CK_CustomerPayments_BankAccount;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_SupplierPayments_BankAccount')
    ALTER TABLE dbo.SupplierPayments DROP CONSTRAINT CK_SupplierPayments_BankAccount;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerPayments_Method')
    ALTER TABLE dbo.CustomerPayments DROP CONSTRAINT CK_CustomerPayments_Method;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_SupplierPayments_Method')
    ALTER TABLE dbo.SupplierPayments DROP CONSTRAINT CK_SupplierPayments_Method;

IF COL_LENGTH(N'dbo.CustomerPayments', N'PaymentMethod') IS NOT NULL
    ALTER TABLE dbo.CustomerPayments DROP COLUMN PaymentMethod;
IF COL_LENGTH(N'dbo.CustomerPayments', N'BankAccountId') IS NOT NULL
    ALTER TABLE dbo.CustomerPayments DROP COLUMN BankAccountId;
IF COL_LENGTH(N'dbo.CustomerPayments', N'Reference') IS NOT NULL
    ALTER TABLE dbo.CustomerPayments DROP COLUMN Reference;
IF COL_LENGTH(N'dbo.SupplierPayments', N'PaymentMethod') IS NOT NULL
    ALTER TABLE dbo.SupplierPayments DROP COLUMN PaymentMethod;
IF COL_LENGTH(N'dbo.SupplierPayments', N'BankAccountId') IS NOT NULL
    ALTER TABLE dbo.SupplierPayments DROP COLUMN BankAccountId;
IF COL_LENGTH(N'dbo.SupplierPayments', N'Reference') IS NOT NULL
    ALTER TABLE dbo.SupplierPayments DROP COLUMN Reference;

COMMIT TRANSACTION;
