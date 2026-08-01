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
                AND [CashDifference]=[CountedCash]-[ExpectedCash]))
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
    CONSTRAINT [PK_WorkSessionClosurePaymentTotals]
        PRIMARY KEY CLUSTERED ([WorkSessionClosureId],[PaymentMethodCode]),
    CONSTRAINT [FK_WorkSessionClosurePaymentTotals_Closures]
        FOREIGN KEY ([WorkSessionClosureId])
        REFERENCES [dbo].[WorkSessionClosures] ([WorkSessionClosureId])
);
GO
