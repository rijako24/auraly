CREATE TABLE [dbo].[WorkSessionMovements]
(
    [WorkSessionMovementId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
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
    CONSTRAINT [PK_WorkSessionMovements] PRIMARY KEY CLUSTERED ([WorkSessionMovementId]),
    CONSTRAINT [FK_WorkSessionMovements_Sessions] FOREIGN KEY ([WorkSessionId])
        REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_WorkSessionMovements_SalesPayments]
        FOREIGN KEY ([DocumentId],[PaymentNumber])
        REFERENCES [dbo].[SalesPayments] ([DocumentId],[PaymentNumber]),
    CONSTRAINT [FK_WorkSessionMovements_RecordedBy] FOREIGN KEY ([RecordedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_WorkSessionMovements_Source] UNIQUE ([WorkSessionId],[SourceKey]),
    CONSTRAINT [CK_WorkSessionMovements_Type] CHECK (
        [MovementType] IN
            (N'SalePayment',N'Refund',N'ReceivablePayment',N'PayablePayment',N'OpeningFloat',N'CashIn',N'CashOut',N'Adjustment')),
    CONSTRAINT [CK_WorkSessionMovements_Amount] CHECK ([Amount] <> 0),
    CONSTRAINT [CK_WorkSessionMovements_SaleSource] CHECK (
        ([MovementType]=N'SalePayment' AND [DocumentId] IS NOT NULL AND [PaymentNumber] IS NOT NULL)
        OR ([MovementType]=N'Refund' AND
            (([DocumentId] IS NULL AND [PaymentNumber] IS NULL)
             OR ([DocumentId] IS NOT NULL AND [PaymentNumber] IS NOT NULL)))
        OR ([MovementType] NOT IN (N'SalePayment',N'Refund')))
);
GO

CREATE INDEX [IX_WorkSessionMovements_Session_Occurred]
    ON [dbo].[WorkSessionMovements] ([WorkSessionId],[OccurredAt]);
GO

CREATE INDEX [IX_WorkSessionMovements_Session_BusinessDate]
    ON [dbo].[WorkSessionMovements] ([WorkSessionId],[BusinessDate],[PaymentMethodCode]);
GO
