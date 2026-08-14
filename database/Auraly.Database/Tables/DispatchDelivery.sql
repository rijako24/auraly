CREATE TABLE [dbo].[DispatchDeliveryPayments] (
    [DispatchDeliveryPaymentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchSourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationType] NVARCHAR(24) NOT NULL,
    [PaymentMethod] NVARCHAR(24) NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [Reference] NVARCHAR(120) NULL,
    [EvidenceUrl] NVARCHAR(1000) NULL,
    [RecordedBy] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DispatchDeliveryPayments] PRIMARY KEY ([DispatchDeliveryPaymentId]),
    CONSTRAINT [FK_DispatchDeliveryPayments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DispatchDeliveryPayments_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchDeliveryPayments_Documents] FOREIGN KEY ([DispatchSourceDocumentId]) REFERENCES [dbo].[DispatchSourceDocuments] ([DispatchSourceDocumentId]),
    CONSTRAINT [FK_DispatchDeliveryPayments_Users] FOREIGN KEY ([RecordedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_DispatchDeliveryPayments_Application] CHECK ([ApplicationType] IN (N'InvoicePayment',N'CreditDocument',N'CreditAdvance')),
    CONSTRAINT [CK_DispatchDeliveryPayments_Shape] CHECK (
      ([ApplicationType]=N'CreditDocument' AND [PaymentMethod] IS NULL AND [Amount]=0 AND [EvidenceUrl] IS NOT NULL)
      OR ([ApplicationType] IN (N'InvoicePayment',N'CreditAdvance') AND [PaymentMethod] IN (N'Cash',N'Deposit') AND [Amount]>0
          AND ([PaymentMethod]=N'Cash' OR [EvidenceUrl] IS NOT NULL)))
);
GO
CREATE INDEX [IX_DispatchDeliveryPayments_Dispatch_Document] ON [dbo].[DispatchDeliveryPayments] ([DispatchId],[DispatchSourceDocumentId]);
GO

CREATE TABLE [dbo].[DispatchDeliveryReturns] (
    [DispatchDeliveryReturnId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchSourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalLineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [InventoryDisposition] NVARCHAR(24) NOT NULL,
    [ReasonCode] NVARCHAR(32) NOT NULL,
    [ReasonDescription] NVARCHAR(300) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DispatchDeliveryReturns] PRIMARY KEY ([DispatchDeliveryReturnId]),
    CONSTRAINT [FK_DispatchDeliveryReturns_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DispatchDeliveryReturns_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchDeliveryReturns_Documents] FOREIGN KEY ([DispatchSourceDocumentId]) REFERENCES [dbo].[DispatchSourceDocuments] ([DispatchSourceDocumentId]),
    CONSTRAINT [FK_DispatchDeliveryReturns_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_DispatchDeliveryReturns_Users] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_DispatchDeliveryReturns_Line] UNIQUE ([DispatchSourceDocumentId],[OriginalLineNumber]),
    CONSTRAINT [CK_DispatchDeliveryReturns_Quantity] CHECK ([Quantity]>0),
    CONSTRAINT [CK_DispatchDeliveryReturns_Disposition] CHECK ([InventoryDisposition] IN (N'Sellable',N'NotReturned'))
);
GO

CREATE TABLE [dbo].[DispatchDeliveryEvents] (
    [DispatchDeliveryEventId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchSourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DeliveryStatus] NVARCHAR(24) NOT NULL,
    [Reason] NVARCHAR(160) NULL,
    [Notes] NVARCHAR(500) NULL,
    [Latitude] DECIMAL(9,6) NULL,
    [Longitude] DECIMAL(9,6) NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [RecordedBy] UNIQUEIDENTIFIER NOT NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    CONSTRAINT [PK_DispatchDeliveryEvents] PRIMARY KEY ([DispatchDeliveryEventId]),
    CONSTRAINT [FK_DispatchDeliveryEvents_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DispatchDeliveryEvents_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchDeliveryEvents_Documents] FOREIGN KEY ([DispatchSourceDocumentId]) REFERENCES [dbo].[DispatchSourceDocuments] ([DispatchSourceDocumentId]),
    CONSTRAINT [FK_DispatchDeliveryEvents_Users] FOREIGN KEY ([RecordedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_DispatchDeliveryEvents_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_DispatchDeliveryEvents_Status] CHECK ([DeliveryStatus] IN (N'Delivered',N'PartiallyDelivered',N'NotDelivered')),
    CONSTRAINT [CK_DispatchDeliveryEvents_Reason] CHECK ([DeliveryStatus]<>N'NotDelivered' OR [Reason] IS NOT NULL)
);
GO
CREATE UNIQUE INDEX [UX_DispatchDeliveryEvents_Document] ON [dbo].[DispatchDeliveryEvents] ([DispatchSourceDocumentId]);
GO

CREATE TABLE [dbo].[DispatchDocumentSequences] (
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchSourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [Sequence] INT NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DispatchDocumentSequences] PRIMARY KEY ([DispatchId],[DispatchSourceDocumentId]),
    CONSTRAINT [FK_DispatchDocumentSequences_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchDocumentSequences_Documents] FOREIGN KEY ([DispatchSourceDocumentId]) REFERENCES [dbo].[DispatchSourceDocuments] ([DispatchSourceDocumentId]),
    CONSTRAINT [FK_DispatchDocumentSequences_Users] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_DispatchDocumentSequences_Sequence] CHECK ([Sequence]>0),
    CONSTRAINT [UQ_DispatchDocumentSequences_Order] UNIQUE ([DispatchId],[Sequence])
);
GO

CREATE TABLE [dbo].[DispatchSettlements] (
    [DispatchSettlementId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [ExpectedCash] DECIMAL(19,4) NOT NULL,
    [DeclaredCash] DECIMAL(19,4) NOT NULL,
    [CashDifference] AS (COALESCE([CashReceived],[DeclaredCash])-[ExpectedCash]) PERSISTED,
    [DepositTotal] DECIMAL(19,4) NOT NULL,
    [CreditDocumentTotal] DECIMAL(19,4) NOT NULL,
    [CreditAdvanceTotal] DECIMAL(19,4) NOT NULL,
    [ReturnTotal] DECIMAL(19,4) NOT NULL,
    [DifferenceReason] NVARCHAR(500) NULL,
    [TransporterClosedBy] UNIQUEIDENTIFIER NOT NULL,
    [TransporterClosedAt] DATETIMEOFFSET(7) NOT NULL,
    [CashReceived] DECIMAL(19,4) NULL,
    [ReceivedBy] UNIQUEIDENTIFIER NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NULL,
    [Notes] NVARCHAR(500) NULL,
    [Status] NVARCHAR(32) NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    CONSTRAINT [PK_DispatchSettlements] PRIMARY KEY ([DispatchSettlementId]),
    CONSTRAINT [FK_DispatchSettlements_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DispatchSettlements_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchSettlements_ClosedBy] FOREIGN KEY ([TransporterClosedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_DispatchSettlements_ReceivedBy] FOREIGN KEY ([ReceivedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_DispatchSettlements_Dispatch] UNIQUE ([DispatchId]),
    CONSTRAINT [UQ_DispatchSettlements_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_DispatchSettlements_Values] CHECK ([ExpectedCash]>=0 AND [DeclaredCash]>=0 AND [DepositTotal]>=0 AND [CreditDocumentTotal]>=0 AND [CreditAdvanceTotal]>=0 AND [ReturnTotal]>=0),
    CONSTRAINT [CK_DispatchSettlements_Difference] CHECK ([DeclaredCash]=[ExpectedCash] OR [DifferenceReason] IS NOT NULL),
    CONSTRAINT [CK_DispatchSettlements_Status] CHECK ([Status] IN (N'PendingReview',N'Processing',N'Completed'))
);
GO
