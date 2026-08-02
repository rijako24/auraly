CREATE TABLE [dbo].[OrderClaims] (
    [OrderClaimId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ClaimedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [ReleasedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_OrderClaims] PRIMARY KEY ([OrderClaimId]),
    CONSTRAINT [FK_OrderClaims_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_OrderClaims_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_OrderClaims_Orders] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Orders] ([OrderId]),
    CONSTRAINT [FK_OrderClaims_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_OrderClaims_Devices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [FK_OrderClaims_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_OrderClaims_Expiration] CHECK ([ExpiresAt] > [ClaimedAt])
);
GO

CREATE UNIQUE INDEX [UX_OrderClaims_ActiveOrder]
    ON [dbo].[OrderClaims] ([OrderId]) WHERE [ReleasedAt] IS NULL;
GO

CREATE INDEX [IX_OrderClaims_Business_Expires]
    ON [dbo].[OrderClaims] ([BusinessId], [ExpiresAt])
    INCLUDE ([OrderId], [WorkSessionId], [UserId], [ReleasedAt]);
GO

CREATE TABLE [dbo].[OrderInvoiceBatchReceipts] (
    [OperationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(100) NOT NULL,
    [RequestHash] CHAR(64) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [RequestedCount] INT NOT NULL,
    [CompletedCount] INT NOT NULL CONSTRAINT [DF_OrderInvoiceBatchReceipts_Completed] DEFAULT 0,
    [FailedCount] INT NOT NULL CONSTRAINT [DF_OrderInvoiceBatchReceipts_Failed] DEFAULT 0,
    [ResultJson] NVARCHAR(MAX) NULL,
    [LeaseToken] UNIQUEIDENTIFIER NOT NULL,
    [LeaseExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_OrderInvoiceBatchReceipts] PRIMARY KEY ([OperationId]),
    CONSTRAINT [FK_OrderInvoiceBatchReceipts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_OrderInvoiceBatchReceipts_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_OrderInvoiceBatchReceipts_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_OrderInvoiceBatchReceipts_Devices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [FK_OrderInvoiceBatchReceipts_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_OrderInvoiceBatchReceipts_Business_Key] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [CK_OrderInvoiceBatchReceipts_Status] CHECK ([Status] IN (N'Processing', N'PartiallyCompleted', N'Completed', N'Failed')),
    CONSTRAINT [CK_OrderInvoiceBatchReceipts_Counts] CHECK ([RequestedCount] > 0 AND [CompletedCount] >= 0 AND [FailedCount] >= 0),
    CONSTRAINT [CK_OrderInvoiceBatchReceipts_Lease] CHECK ([LeaseExpiresAt] >= [CreatedAt])
);
GO

CREATE TABLE [dbo].[OrderInvoiceLinks] (
    [OrderInvoiceLinkId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [OperationId] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_OrderInvoiceLinks] PRIMARY KEY ([OrderInvoiceLinkId]),
    CONSTRAINT [FK_OrderInvoiceLinks_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_OrderInvoiceLinks_Orders] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Orders] ([OrderId]),
    CONSTRAINT [FK_OrderInvoiceLinks_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_OrderInvoiceLinks_Batches] FOREIGN KEY ([OperationId]) REFERENCES [dbo].[OrderInvoiceBatchReceipts] ([OperationId]),
    CONSTRAINT [UQ_OrderInvoiceLinks_Order] UNIQUE ([OrderId]),
    CONSTRAINT [UQ_OrderInvoiceLinks_Document] UNIQUE ([DocumentId])
);
GO

CREATE INDEX [IX_OrderInvoiceLinks_Business_Created]
    ON [dbo].[OrderInvoiceLinks] ([BusinessId], [CreatedAt])
    INCLUDE ([OrderId], [DocumentId], [OperationId]);
GO