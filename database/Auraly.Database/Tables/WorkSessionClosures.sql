CREATE TABLE [dbo].[WorkSessionClosures]
(
    [WorkSessionClosureId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [ClosedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [TotalSales] DECIMAL(19,4) NOT NULL,
    [TotalRefunds] DECIMAL(19,4) NOT NULL,
    [TotalOther] DECIMAL(19,4) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [ExpectedCash] DECIMAL(19,4) NOT NULL,
    [CountedCash] DECIMAL(19,4) NULL,
    [CashDifference] DECIMAL(19,4) NULL,
    [SalesCount] BIGINT NOT NULL CONSTRAINT [DF_WorkSessionClosures_SalesCount] DEFAULT(0),
    [CreditSalesCount] INT NOT NULL CONSTRAINT [DF_WorkSessionClosures_CreditSalesCount] DEFAULT(0),
    [CreditSalesAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_WorkSessionClosures_CreditSalesAmount] DEFAULT(0),
    [ReturnCount] BIGINT NOT NULL CONSTRAINT [DF_WorkSessionClosures_ReturnCount] DEFAULT(0),
    [ReconciliationStatus] NVARCHAR(32) NOT NULL CONSTRAINT [DF_WorkSessionClosures_ReconciliationStatus] DEFAULT N'Pending',
    [Note] NVARCHAR(500) NULL,
    [ReceiptSnapshotJson] NVARCHAR(MAX) NOT NULL,
    [ReceiptHash] VARBINARY(32) NOT NULL,
    [ClosedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_WorkSessionClosures]
        PRIMARY KEY CLUSTERED ([WorkSessionClosureId]),
    CONSTRAINT [FK_WorkSessionClosures_Sessions]
        FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_WorkSessionClosures_Users]
        FOREIGN KEY ([ClosedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_WorkSessionClosures_Session] UNIQUE ([WorkSessionId]),
    CONSTRAINT [UQ_WorkSessionClosures_Idempotency]
        UNIQUE ([ClosedByUserId],[IdempotencyKey]),
    CONSTRAINT [CK_WorkSessionClosures_CountedCash]
        CHECK ([CountedCash] IS NULL OR [CountedCash] >= 0),
    CONSTRAINT [CK_WorkSessionClosures_Difference]
        CHECK (([CountedCash] IS NULL AND [CashDifference] IS NULL)
            OR ([CountedCash] IS NOT NULL
                AND [CashDifference]=[CountedCash]-[ExpectedCash])),
    CONSTRAINT [CK_WorkSessionClosures_ReconciliationStatus] CHECK ([ReconciliationStatus] IN (N'Pending',N'Partial',N'Reconciled',N'ReconciledWithDifferences'))
);
GO

CREATE TABLE [dbo].[WorkSessionClosureReconciliations]
(
    [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionClosureId] UNIQUEIDENTIFIER NOT NULL,
    [ReconciledByUserId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [Status] NVARCHAR(32) NOT NULL,
    [Note] NVARCHAR(500) NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [SnapshotHash] BINARY(32) NOT NULL,
    [ReconciledAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_WorkSessionClosureReconciliations] PRIMARY KEY CLUSTERED ([ReconciliationId]),
    CONSTRAINT [FK_WorkSessionClosureReconciliations_Closure] FOREIGN KEY ([WorkSessionClosureId]) REFERENCES [dbo].[WorkSessionClosures]([WorkSessionClosureId]),
    CONSTRAINT [FK_WorkSessionClosureReconciliations_User] FOREIGN KEY ([ReconciledByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [UQ_WorkSessionClosureReconciliations_Closure] UNIQUE ([WorkSessionClosureId]),
    CONSTRAINT [UQ_WorkSessionClosureReconciliations_Idempotency] UNIQUE ([ReconciledByUserId],[IdempotencyKey]),
    CONSTRAINT [CK_WorkSessionClosureReconciliations_Status] CHECK ([Status] IN (N'Reconciled',N'ReconciledWithDifferences'))
);
GO

CREATE TABLE [dbo].[WorkSessionClosureReconciliationLines]
(
    [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentMethodCode] NVARCHAR(32) NOT NULL,
    [ExpectedAmount] DECIMAL(19,4) NOT NULL,
    [CountedAmount] DECIMAL(19,4) NULL,
    [VerifiedAmount] DECIMAL(19,4) NOT NULL,
    [Difference] DECIMAL(19,4) NOT NULL,
    [IsConfirmed] BIT NOT NULL,
    [ReasonCode] NVARCHAR(40) NULL,
    CONSTRAINT [PK_WorkSessionClosureReconciliationLines] PRIMARY KEY CLUSTERED ([ReconciliationId],[PaymentMethodCode]),
    CONSTRAINT [FK_WorkSessionClosureReconciliationLines_Header] FOREIGN KEY ([ReconciliationId]) REFERENCES [dbo].[WorkSessionClosureReconciliations]([ReconciliationId]),
    CONSTRAINT [CK_WorkSessionClosureReconciliationLines_Amounts] CHECK ([VerifiedAmount]>=0 AND ([CountedAmount] IS NULL OR [CountedAmount]>=0) AND [Difference]=[VerifiedAmount]-[ExpectedAmount])
);
GO

CREATE TABLE [dbo].[WorkSessionClosureReclassifications]
(
    [ReclassificationId] UNIQUEIDENTIFIER NOT NULL,
    [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [FromPaymentMethodCode] NVARCHAR(32) NOT NULL,
    [ToPaymentMethodCode] NVARCHAR(32) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_WorkSessionClosureReclassifications] PRIMARY KEY CLUSTERED ([ReclassificationId]),
    CONSTRAINT [FK_WorkSessionClosureReclassifications_Header] FOREIGN KEY ([ReconciliationId]) REFERENCES [dbo].[WorkSessionClosureReconciliations]([ReconciliationId]),
    CONSTRAINT [UQ_WorkSessionClosureReclassifications_Line] UNIQUE ([ReconciliationId],[LineNumber]),
    CONSTRAINT [CK_WorkSessionClosureReclassifications_Value] CHECK ([LineNumber]>0 AND [Amount]>0 AND [FromPaymentMethodCode]<>[ToPaymentMethodCode])
);
GO

CREATE TABLE [dbo].[WorkSessionClosurePaymentTotals]
(
    [WorkSessionClosureId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentMethodCode] NVARCHAR(32) NOT NULL,
    [SalesAmount] DECIMAL(19,4) NOT NULL,
    [RefundAmount] DECIMAL(19,4) NOT NULL,
    [OtherAmount] DECIMAL(19,4) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [CountedAmount] DECIMAL(19,4) NULL,
    [Difference] DECIMAL(19,4) NULL,
    CONSTRAINT [PK_WorkSessionClosurePaymentTotals]
        PRIMARY KEY CLUSTERED ([WorkSessionClosureId],[PaymentMethodCode]),
    CONSTRAINT [FK_WorkSessionClosurePaymentTotals_Closures]
        FOREIGN KEY ([WorkSessionClosureId])
        REFERENCES [dbo].[WorkSessionClosures] ([WorkSessionClosureId]),
    CONSTRAINT [CK_WorkSessionClosurePaymentTotals_Counted]
        CHECK ([CountedAmount] IS NULL OR [CountedAmount] >= 0),
    CONSTRAINT [CK_WorkSessionClosurePaymentTotals_Difference]
        CHECK (([CountedAmount] IS NULL AND [Difference] IS NULL)
            OR ([CountedAmount] IS NOT NULL AND [Difference]=[CountedAmount]-[NetAmount]))
);
GO
