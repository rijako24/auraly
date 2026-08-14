CREATE TABLE [dbo].[CashMovementDocuments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [ReasonId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(64) NOT NULL,
    [DocumentNumber] NVARCHAR(80) NOT NULL,
    [DocumentPrefix] NVARCHAR(16) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [Direction] NVARCHAR(8) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [Reference] NVARCHAR(160) NULL,
    [Notes] NVARCHAR(500) NULL,
    [CostCenterId] UNIQUEIDENTIFIER NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [RequestHash] BINARY(32) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [Status] NVARCHAR(32) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CashMovementDocuments] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [UQ_CashMovementDocuments_Business_Document] UNIQUE ([BusinessId],[DocumentId]),
    CONSTRAINT [UQ_CashMovementDocuments_Business_Number] UNIQUE ([BusinessId],[DocumentNumber]),
    CONSTRAINT [UQ_CashMovementDocuments_Business_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [FK_CashMovementDocuments_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CashMovementDocuments_Sessions] FOREIGN KEY ([WorkSessionId])
        REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_CashMovementDocuments_Reasons] FOREIGN KEY ([BusinessId],[ReasonId])
        REFERENCES [dbo].[CashMovementReasons] ([BusinessId],[ReasonId]),
    CONSTRAINT [FK_CashMovementDocuments_Series] FOREIGN KEY ([DocumentSeriesId])
        REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_CashMovementDocuments_CostCenters] FOREIGN KEY ([CostCenterId])
        REFERENCES [dbo].[AccountingCostCenters] ([CostCenterId]),
    CONSTRAINT [FK_CashMovementDocuments_Users] FOREIGN KEY ([ConfirmedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_CashMovementDocuments_Type] CHECK (
        [DocumentType] IN (N'CashReceipt',N'CashDisbursement')),
    CONSTRAINT [CK_CashMovementDocuments_Direction] CHECK ([Direction] IN (N'In',N'Out')),
    CONSTRAINT [CK_CashMovementDocuments_TypeDirection] CHECK (
        ([DocumentType]=N'CashReceipt' AND [Direction]=N'In')
        OR ([DocumentType]=N'CashDisbursement' AND [Direction]=N'Out')),
    CONSTRAINT [CK_CashMovementDocuments_Amount] CHECK ([Amount]>0),
    CONSTRAINT [CK_CashMovementDocuments_Status] CHECK ([Status] IN (N'Accepted',N'Processed'))
);
GO

CREATE INDEX [IX_CashMovementDocuments_Session_Occurred]
    ON [dbo].[CashMovementDocuments] ([WorkSessionId],[OccurredAt],[DocumentId])
    INCLUDE ([Direction],[Amount],[ReasonId],[Status]);
GO
