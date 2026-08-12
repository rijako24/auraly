CREATE TABLE [dbo].[ExternalCustomerReconciliationReceipts]
(
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [ExternalCommerceCustomerId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ResultStatus] NVARCHAR(16) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ExternalCustomerReconciliationReceipts]
        PRIMARY KEY CLUSTERED ([MessageId]),
    CONSTRAINT [FK_ExternalCustomerReconciliationReceipts_ExternalCustomer]
        FOREIGN KEY ([ExternalCommerceCustomerId]) REFERENCES [dbo].[ExternalCommerceCustomers] ([ExternalCommerceCustomerId]),
    CONSTRAINT [FK_ExternalCustomerReconciliationReceipts_Business]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_ExternalCustomerReconciliationReceipts_ResultStatus]
        CHECK ([ResultStatus] IN (N'Linked', N'Conflict'))
);
GO

CREATE INDEX [IX_ExternalCustomerReconciliationReceipts_Customer]
    ON [dbo].[ExternalCustomerReconciliationReceipts]
       ([BusinessId], [ExternalCommerceCustomerId], [ProcessedAt]);
GO
