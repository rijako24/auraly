SET NOCOUNT ON;

IF COL_LENGTH('dbo.Orders', 'PaymentTransactionId') IS NULL
    ALTER TABLE dbo.Orders ADD [PaymentTransactionId] UNIQUEIDENTIFIER NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'OrderId') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE o
        SET PaymentTransactionId = pt.PaymentTransactionId
        FROM dbo.Orders o
        INNER JOIN dbo.PaymentTransactions pt ON pt.OrderId = o.OrderId
        WHERE o.PaymentTransactionId IS NULL;';

    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_PaymentTransactions_Orders'
          AND parent_object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    BEGIN
        ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT [FK_PaymentTransactions_Orders];
    END

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_PaymentTransactions_OrderId'
          AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    BEGIN
        DROP INDEX [IX_PaymentTransactions_OrderId] ON dbo.PaymentTransactions;
    END

    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [OrderId];
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_Orders_PaymentTransactions'
      AND parent_object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    ALTER TABLE dbo.Orders
    ADD CONSTRAINT [FK_Orders_PaymentTransactions]
        FOREIGN KEY ([PaymentTransactionId])
        REFERENCES [dbo].[PaymentTransactions] ([PaymentTransactionId])
        ON DELETE NO ACTION;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_Orders_PaymentTransactionId'
      AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    CREATE UNIQUE INDEX [UX_Orders_PaymentTransactionId]
        ON [dbo].[Orders] ([PaymentTransactionId])
        WHERE [PaymentTransactionId] IS NOT NULL;
END

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_Orders_Status'
      AND parent_object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    ALTER TABLE dbo.Orders DROP CONSTRAINT [CK_Orders_Status];
END

ALTER TABLE dbo.Orders
ADD CONSTRAINT [CK_Orders_Status]
    CHECK ([Status] IN (0, 1, 2, 3, 4, 5, 6, 7, 91));

GO
