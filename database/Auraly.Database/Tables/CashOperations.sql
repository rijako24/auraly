CREATE TABLE [dbo].[CashCounts]
(
    [CashCountId] UNIQUEIDENTIFIER NOT NULL,
    [CashSessionId] UNIQUEIDENTIFIER NOT NULL,
    [CashierShiftId] UNIQUEIDENTIFIER NOT NULL,
    [CountType] NVARCHAR(24) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [CountNumber] NVARCHAR(64) NULL,
    [CountConsecutive] BIGINT NULL,
    [CountedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [ReceivedByUserId] UNIQUEIDENTIFIER NULL,
    [AuthorizedByUserId] UNIQUEIDENTIFIER NULL,
    [ExpectedCalculatedAt] DATETIMEOFFSET(7) NOT NULL,
    [StartedAt] DATETIMEOFFSET(7) NOT NULL,
    [ConfirmedAt] DATETIMEOFFSET(7) NULL,
    [Observation] NVARCHAR(500) NULL,
    [DifferenceReason] NVARCHAR(300) NULL,
    [ReceiptSnapshotJson] NVARCHAR(MAX) NULL,
    [ReceiptHash] BINARY(32) NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CashCounts] PRIMARY KEY CLUSTERED ([CashCountId]),
    CONSTRAINT [FK_CashCounts_Sessions] FOREIGN KEY ([CashSessionId])
        REFERENCES [dbo].[CashSessions] ([CashSessionId]),
    CONSTRAINT [FK_CashCounts_Shifts] FOREIGN KEY ([CashSessionId],[CashierShiftId])
        REFERENCES [dbo].[CashierShifts] ([CashSessionId],[CashierShiftId]),
    CONSTRAINT [FK_CashCounts_CountedBy] FOREIGN KEY ([CountedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_CashCounts_ReceivedBy] FOREIGN KEY ([ReceivedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_CashCounts_AuthorizedBy] FOREIGN KEY ([AuthorizedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_CashCounts_Type] CHECK (
        ([CountType]=N'Handoff' AND [ReceivedByUserId] IS NOT NULL
            AND [AuthorizedByUserId] IS NOT NULL
            AND [CountNumber] IS NULL AND [CountConsecutive] IS NULL)
        OR
        ([CountType]=N'Final' AND [ReceivedByUserId] IS NULL
            AND [AuthorizedByUserId] IS NULL)),
    CONSTRAINT [CK_CashCounts_Status] CHECK (
        ([Status]=N'Draft' AND [ConfirmedAt] IS NULL)
        OR
        ([Status]=N'Confirmed' AND [ConfirmedAt] IS NOT NULL)),
    CONSTRAINT [CK_CashCounts_FinalReceipt] CHECK (
        [CountType]<>N'Final' OR [Status]<>N'Confirmed' OR
        ([CountNumber] IS NOT NULL AND [CountConsecutive] IS NOT NULL AND
         [ReceiptSnapshotJson] IS NOT NULL AND [ReceiptHash] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_CashCounts_Shift_Handoff]
    ON [dbo].[CashCounts] ([CashierShiftId])
    WHERE [CountType]=N'Handoff' AND [Status]=N'Confirmed';
GO

CREATE UNIQUE INDEX [UX_CashCounts_Session_Final]
    ON [dbo].[CashCounts] ([CashSessionId])
    WHERE [CountType]=N'Final' AND [Status]=N'Confirmed';
GO

CREATE UNIQUE INDEX [UX_CashCounts_Number]
    ON [dbo].[CashCounts] ([CountNumber]) WHERE [CountNumber] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_CashCounts_Session_Idempotency]
    ON [dbo].[CashCounts] ([CashSessionId],[IdempotencyKey]);
GO

CREATE TABLE [dbo].[CashCountLines]
(
    [CashCountId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentMethodCode] NVARCHAR(32) NOT NULL,
    [ExpectedAmount] DECIMAL(19,4) NOT NULL,
    [CountedAmount] DECIMAL(19,4) NOT NULL,
    [DifferenceAmount] AS ([CountedAmount]-[ExpectedAmount]) PERSISTED,
    CONSTRAINT [PK_CashCountLines]
        PRIMARY KEY CLUSTERED ([CashCountId],[PaymentMethodCode]),
    CONSTRAINT [FK_CashCountLines_Counts] FOREIGN KEY ([CashCountId])
        REFERENCES [dbo].[CashCounts] ([CashCountId]),
    CONSTRAINT [CK_CashCountLines_Amounts]
        CHECK ([ExpectedAmount] >= 0 AND [CountedAmount] >= 0)
);
GO

CREATE TABLE [dbo].[CashMovements]
(
    [CashMovementId] UNIQUEIDENTIFIER NOT NULL,
    [CashSessionId] UNIQUEIDENTIFIER NOT NULL,
    [CashierShiftId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NULL,
    [PaymentNumber] INT NULL,
    [BusinessDate] DATE NOT NULL,
    [MovementType] NVARCHAR(32) NOT NULL,
    [PaymentMethodCode] NVARCHAR(32) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [Reference] NVARCHAR(160) NULL,
    [SourceKey] NVARCHAR(160) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [RecordedByUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_CashMovements] PRIMARY KEY CLUSTERED ([CashMovementId]),
    CONSTRAINT [FK_CashMovements_Sessions] FOREIGN KEY ([CashSessionId])
        REFERENCES [dbo].[CashSessions] ([CashSessionId]),
    CONSTRAINT [FK_CashMovements_Shifts] FOREIGN KEY ([CashSessionId],[CashierShiftId])
        REFERENCES [dbo].[CashierShifts] ([CashSessionId],[CashierShiftId]),
    CONSTRAINT [FK_CashMovements_SalesPayments] FOREIGN KEY ([DocumentId],[PaymentNumber])
        REFERENCES [dbo].[SalesPayments] ([DocumentId],[PaymentNumber]),
    CONSTRAINT [FK_CashMovements_RecordedBy] FOREIGN KEY ([RecordedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_CashMovements_Session_Source] UNIQUE ([CashSessionId],[SourceKey]),
    CONSTRAINT [CK_CashMovements_Type] CHECK (
        [MovementType] IN
            (N'SalePayment',N'Refund',N'ReceivablePayment',N'CashIn',N'CashOut',N'Adjustment')),
    CONSTRAINT [CK_CashMovements_Amount] CHECK ([Amount] <> 0),
    CONSTRAINT [CK_CashMovements_SaleSource] CHECK (
        ([MovementType] IN (N'SalePayment',N'Refund')
            AND [DocumentId] IS NOT NULL AND [PaymentNumber] IS NOT NULL)
        OR
        ([MovementType] NOT IN (N'SalePayment',N'Refund')))
);
GO

CREATE INDEX [IX_CashMovements_Session_Shift_Occurred]
    ON [dbo].[CashMovements] ([CashSessionId],[CashierShiftId],[OccurredAt]);
GO

CREATE INDEX [IX_CashMovements_Session_BusinessDate]
    ON [dbo].[CashMovements] ([CashSessionId],[BusinessDate],[PaymentMethodCode]);
GO
