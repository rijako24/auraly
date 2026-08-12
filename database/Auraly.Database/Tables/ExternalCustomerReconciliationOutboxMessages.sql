CREATE TABLE [dbo].[ExternalCustomerReconciliationOutboxMessages]
(
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [ExternalCommerceCustomerId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [AvailableAt] DATETIMEOFFSET(7) NOT NULL,
    [PublishedAt] DATETIMEOFFSET(7) NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_ExternalCustomerReconciliationOutboxMessages_AttemptCount] DEFAULT (0),
    [LastAttemptAt] DATETIMEOFFSET(7) NULL,
    [LastError] NVARCHAR(1000) NULL,
    [LeaseId] UNIQUEIDENTIFIER NULL,
    [LeaseExpiresAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_ExternalCustomerReconciliationOutboxMessages]
        PRIMARY KEY CLUSTERED ([MessageId]),
    CONSTRAINT [FK_ExternalCustomerReconciliationOutboxMessages_ExternalCustomer]
        FOREIGN KEY ([ExternalCommerceCustomerId]) REFERENCES [dbo].[ExternalCommerceCustomers] ([ExternalCommerceCustomerId]),
    CONSTRAINT [FK_ExternalCustomerReconciliationOutboxMessages_Business]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_ExternalCustomerReconciliationOutboxMessages_AttemptCount]
        CHECK ([AttemptCount] >= 0),
    CONSTRAINT [CK_ExternalCustomerReconciliationOutboxMessages_Lease]
        CHECK (([LeaseId] IS NULL AND [LeaseExpiresAt] IS NULL) OR
               ([LeaseId] IS NOT NULL AND [LeaseExpiresAt] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_ExternalCustomerReconciliationOutboxMessages_PendingCustomer]
    ON [dbo].[ExternalCustomerReconciliationOutboxMessages] ([ExternalCommerceCustomerId])
    WHERE [PublishedAt] IS NULL;
GO

CREATE INDEX [IX_ExternalCustomerReconciliationOutboxMessages_Dispatch]
    ON [dbo].[ExternalCustomerReconciliationOutboxMessages]
       ([PublishedAt], [AvailableAt], [LeaseExpiresAt], [OccurredAt]);
GO
