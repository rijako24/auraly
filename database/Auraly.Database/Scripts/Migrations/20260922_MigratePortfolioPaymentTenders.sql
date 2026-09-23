SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM dbo.CustomerPayments WHERE Status=N'Accepted')
    OR EXISTS (SELECT 1 FROM dbo.SupplierPayments WHERE Status=N'Accepted')
    THROW 51620, N'Process accepted portfolio payments before changing the payment contract.', 1;

IF OBJECT_ID(N'dbo.CustomerPaymentTenders',N'U') IS NULL
    CREATE TABLE dbo.CustomerPaymentTenders
    (
        PaymentId UNIQUEIDENTIFIER NOT NULL,
        LineNumber INT NOT NULL,
        MethodCode NVARCHAR(32) NOT NULL,
        Amount DECIMAL(19,4) NOT NULL,
        TenderedAmount DECIMAL(19,4) NULL,
        BankAccountId UNIQUEIDENTIFIER NULL,
        Reference NVARCHAR(160) NULL,
        Notes NVARCHAR(500) NULL,
        CardFranchiseCode NVARCHAR(64) NULL,
        ApprovalNumber NVARCHAR(100) NULL,
        CONSTRAINT PK_CustomerPaymentTenders PRIMARY KEY CLUSTERED (PaymentId,LineNumber),
        CONSTRAINT FK_CustomerPaymentTenders_Payment FOREIGN KEY (PaymentId) REFERENCES dbo.CustomerPayments(PaymentId),
        CONSTRAINT FK_CustomerPaymentTenders_BankAccount FOREIGN KEY (BankAccountId) REFERENCES accounting.BankAccounts(BankAccountId),
        CONSTRAINT CK_CustomerPaymentTenders_Amount CHECK (LineNumber>0 AND Amount>0),
        CONSTRAINT CK_CustomerPaymentTenders_Cash CHECK (TenderedAmount IS NULL OR (MethodCode=N'Cash' AND TenderedAmount>=Amount))
    );

IF OBJECT_ID(N'dbo.SupplierPaymentTenders',N'U') IS NULL
    CREATE TABLE dbo.SupplierPaymentTenders
    (
        PaymentId UNIQUEIDENTIFIER NOT NULL,
        LineNumber INT NOT NULL,
        MethodCode NVARCHAR(32) NOT NULL,
        Amount DECIMAL(19,4) NOT NULL,
        TenderedAmount DECIMAL(19,4) NULL,
        BankAccountId UNIQUEIDENTIFIER NULL,
        Reference NVARCHAR(160) NULL,
        Notes NVARCHAR(500) NULL,
        CardFranchiseCode NVARCHAR(64) NULL,
        ApprovalNumber NVARCHAR(100) NULL,
        CONSTRAINT PK_SupplierPaymentTenders PRIMARY KEY CLUSTERED (PaymentId,LineNumber),
        CONSTRAINT FK_SupplierPaymentTenders_Payment FOREIGN KEY (PaymentId) REFERENCES dbo.SupplierPayments(PaymentId),
        CONSTRAINT FK_SupplierPaymentTenders_BankAccount FOREIGN KEY (BankAccountId) REFERENCES accounting.BankAccounts(BankAccountId),
        CONSTRAINT CK_SupplierPaymentTenders_Amount CHECK (LineNumber>0 AND Amount>0),
        CONSTRAINT CK_SupplierPaymentTenders_Cash CHECK (TenderedAmount IS NULL OR (MethodCode=N'Cash' AND TenderedAmount>=Amount))
    );
GO

IF COL_LENGTH(N'dbo.CustomerPayments',N'PaymentMethod') IS NOT NULL
    EXEC sys.sp_executesql N'
        INSERT dbo.CustomerPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,BankAccountId,Reference)
        SELECT payment.PaymentId,1,payment.PaymentMethod,payment.TotalAmount,payment.BankAccountId,payment.Reference
        FROM dbo.CustomerPayments payment
        WHERE NOT EXISTS(SELECT 1 FROM dbo.CustomerPaymentTenders tender WHERE tender.PaymentId=payment.PaymentId);';

IF COL_LENGTH(N'dbo.SupplierPayments',N'PaymentMethod') IS NOT NULL
    EXEC sys.sp_executesql N'
        INSERT dbo.SupplierPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,BankAccountId,Reference)
        SELECT payment.PaymentId,1,payment.PaymentMethod,payment.TotalAmount,payment.BankAccountId,payment.Reference
        FROM dbo.SupplierPayments payment
        WHERE NOT EXISTS(SELECT 1 FROM dbo.SupplierPaymentTenders tender WHERE tender.PaymentId=payment.PaymentId);';

IF COL_LENGTH(N'dbo.CustomerPayments',N'PaymentBreakdownJson') IS NOT NULL
    EXEC sys.sp_executesql N'
        INSERT dbo.CustomerPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,
          BankAccountId,Reference,Notes,CardFranchiseCode,ApprovalNumber)
        SELECT payment.PaymentId,tender.LineNumber,tender.MethodCode,tender.Amount,tender.TenderedAmount,
          tender.BankAccountId,tender.Reference,tender.Notes,tender.CardFranchiseCode,tender.ApprovalNumber
        FROM dbo.CustomerPayments payment
        CROSS APPLY OPENJSON(payment.PaymentBreakdownJson) WITH(
          LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),TenderedAmount decimal(19,4),
          BankAccountId uniqueidentifier,Reference nvarchar(160),Notes nvarchar(500),
          CardFranchiseCode nvarchar(64),ApprovalNumber nvarchar(100)) tender
        WHERE NOT EXISTS(SELECT 1 FROM dbo.CustomerPaymentTenders existing WHERE existing.PaymentId=payment.PaymentId);';

IF COL_LENGTH(N'dbo.SupplierPayments',N'PaymentBreakdownJson') IS NOT NULL
    EXEC sys.sp_executesql N'
        INSERT dbo.SupplierPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,
          BankAccountId,Reference,Notes,CardFranchiseCode,ApprovalNumber)
        SELECT payment.PaymentId,tender.LineNumber,tender.MethodCode,tender.Amount,tender.TenderedAmount,
          tender.BankAccountId,tender.Reference,tender.Notes,tender.CardFranchiseCode,tender.ApprovalNumber
        FROM dbo.SupplierPayments payment
        CROSS APPLY OPENJSON(payment.PaymentBreakdownJson) WITH(
          LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),TenderedAmount decimal(19,4),
          BankAccountId uniqueidentifier,Reference nvarchar(160),Notes nvarchar(500),
          CardFranchiseCode nvarchar(64),ApprovalNumber nvarchar(100)) tender
        WHERE NOT EXISTS(SELECT 1 FROM dbo.SupplierPaymentTenders existing WHERE existing.PaymentId=payment.PaymentId);';

IF EXISTS (
    SELECT 1 FROM dbo.CustomerPayments payment
    LEFT JOIN dbo.CustomerPaymentTenders tender ON tender.PaymentId=payment.PaymentId
    GROUP BY payment.PaymentId,payment.TotalAmount
    HAVING COUNT(tender.LineNumber)=0 OR SUM(tender.Amount)<>payment.TotalAmount)
    OR EXISTS (
    SELECT 1 FROM dbo.SupplierPayments payment
    LEFT JOIN dbo.SupplierPaymentTenders tender ON tender.PaymentId=payment.PaymentId
    GROUP BY payment.PaymentId,payment.TotalAmount
    HAVING COUNT(tender.LineNumber)=0 OR SUM(tender.Amount)<>payment.TotalAmount)
    THROW 51621, N'Portfolio payment tender migration is incomplete.', 1;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_CustomerPayments_BankAccounts')
    ALTER TABLE dbo.CustomerPayments DROP CONSTRAINT FK_CustomerPayments_BankAccounts;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SupplierPayments_BankAccounts')
    ALTER TABLE dbo.SupplierPayments DROP CONSTRAINT FK_SupplierPayments_BankAccounts;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_CustomerPayments_BankAccount')
    ALTER TABLE dbo.CustomerPayments DROP CONSTRAINT CK_CustomerPayments_BankAccount;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_SupplierPayments_BankAccount')
    ALTER TABLE dbo.SupplierPayments DROP CONSTRAINT CK_SupplierPayments_BankAccount;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_CustomerPayments_Method')
    ALTER TABLE dbo.CustomerPayments DROP CONSTRAINT CK_CustomerPayments_Method;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_SupplierPayments_Method')
    ALTER TABLE dbo.SupplierPayments DROP CONSTRAINT CK_SupplierPayments_Method;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_CustomerPayments_Breakdown')
    ALTER TABLE dbo.CustomerPayments DROP CONSTRAINT CK_CustomerPayments_Breakdown;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_SupplierPayments_Breakdown')
    ALTER TABLE dbo.SupplierPayments DROP CONSTRAINT CK_SupplierPayments_Breakdown;
IF COL_LENGTH(N'dbo.SupplierPayments',N'PaymentMethod') IS NOT NULL
    AND EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id=OBJECT_ID(N'dbo.SupplierPayments')
                  AND name=N'IX_SupplierPayments_Business_Paid')
    DROP INDEX IX_SupplierPayments_Business_Paid ON dbo.SupplierPayments;

IF COL_LENGTH(N'dbo.CustomerPayments',N'PaymentMethod') IS NOT NULL
    ALTER TABLE dbo.CustomerPayments DROP COLUMN PaymentMethod;
IF COL_LENGTH(N'dbo.CustomerPayments',N'BankAccountId') IS NOT NULL
    ALTER TABLE dbo.CustomerPayments DROP COLUMN BankAccountId;
IF COL_LENGTH(N'dbo.CustomerPayments',N'Reference') IS NOT NULL
    ALTER TABLE dbo.CustomerPayments DROP COLUMN Reference;
IF COL_LENGTH(N'dbo.SupplierPayments',N'PaymentMethod') IS NOT NULL
    ALTER TABLE dbo.SupplierPayments DROP COLUMN PaymentMethod;
IF COL_LENGTH(N'dbo.SupplierPayments',N'BankAccountId') IS NOT NULL
    ALTER TABLE dbo.SupplierPayments DROP COLUMN BankAccountId;
IF COL_LENGTH(N'dbo.SupplierPayments',N'Reference') IS NOT NULL
    ALTER TABLE dbo.SupplierPayments DROP COLUMN Reference;
IF COL_LENGTH(N'dbo.CustomerPayments',N'PaymentBreakdownJson') IS NOT NULL
    ALTER TABLE dbo.CustomerPayments DROP COLUMN PaymentBreakdownJson;
IF COL_LENGTH(N'dbo.SupplierPayments',N'PaymentBreakdownJson') IS NOT NULL
    ALTER TABLE dbo.SupplierPayments DROP COLUMN PaymentBreakdownJson;
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id=OBJECT_ID(N'dbo.SupplierPayments')
                 AND name=N'IX_SupplierPayments_Business_Paid')
    CREATE INDEX IX_SupplierPayments_Business_Paid
        ON dbo.SupplierPayments(BusinessId,PaidAt DESC)
        INCLUDE(SupplierId,Status,TotalAmount);

COMMIT TRANSACTION;
