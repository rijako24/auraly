CREATE TABLE [dbo].[DispatchSettlementOperations] (
    [DispatchSettlementOperationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [CashReceived] DECIMAL(19,4) NOT NULL,
    [Notes] NVARCHAR(500) NULL,
    [RequestedBy] UNIQUEIDENTIFIER NOT NULL,
    [RequestedAt] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [Attempts] INT NOT NULL,
    [NextAttemptAt] DATETIMEOFFSET(7) NOT NULL,
    [LastError] NVARCHAR(2000) NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_DispatchSettlementOperations] PRIMARY KEY ([DispatchSettlementOperationId]),
    CONSTRAINT [FK_DispatchSettlementOperations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DispatchSettlementOperations_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchSettlementOperations_Users] FOREIGN KEY ([RequestedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_DispatchSettlementOperations_Dispatch] UNIQUE ([DispatchId]),
    CONSTRAINT [UQ_DispatchSettlementOperations_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_DispatchSettlementOperations_Status] CHECK ([Status] IN (N'Pending',N'Processing',N'NeedsAttention',N'Completed')),
    CONSTRAINT [CK_DispatchSettlementOperations_Attempts] CHECK ([Attempts]>=0)
);
GO
CREATE INDEX [IX_DispatchSettlementOperations_Queue] ON [dbo].[DispatchSettlementOperations] ([Status],[NextAttemptAt]) INCLUDE ([Attempts],[DispatchId]);
GO
