IF OBJECT_ID(N'dbo.OrderInvoiceBatchReceipts', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.OrderInvoiceBatchReceipts')
          AND name = N'CK_OrderInvoiceBatchReceipts_Status')
        ALTER TABLE dbo.OrderInvoiceBatchReceipts
            DROP CONSTRAINT CK_OrderInvoiceBatchReceipts_Status;

    ALTER TABLE dbo.OrderInvoiceBatchReceipts WITH CHECK
        ADD CONSTRAINT CK_OrderInvoiceBatchReceipts_Status
        CHECK (Status IN (
            N'Processing',
            N'PartiallyCompleted',
            N'Completed',
            N'Failed',
            N'CreditRejected'));

    ALTER TABLE dbo.OrderInvoiceBatchReceipts
        CHECK CONSTRAINT CK_OrderInvoiceBatchReceipts_Status;
END;
GO
