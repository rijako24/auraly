CREATE TABLE [dbo].[DispatchExpenses] (
    [DispatchExpenseId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [Category] NVARCHAR(64) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [Description] NVARCHAR(300) NULL,
    [EvidenceUrl] NVARCHAR(1000) NULL,
    [ApprovalStatus] NVARCHAR(16) NOT NULL,
    [ApprovedAmount] DECIMAL(19,4) NULL,
    [RecordedBy] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ReviewedBy] UNIQUEIDENTIFIER NULL,
    [ReviewedAt] DATETIMEOFFSET(7) NULL,
    [ReviewNotes] NVARCHAR(500) NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [ReviewIdempotencyKey] NVARCHAR(128) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DispatchExpenses] PRIMARY KEY ([DispatchExpenseId]),
    CONSTRAINT [FK_DispatchExpenses_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DispatchExpenses_Dispatches] FOREIGN KEY ([DispatchId]) REFERENCES [dbo].[Dispatches] ([DispatchId]),
    CONSTRAINT [FK_DispatchExpenses_RecordedBy] FOREIGN KEY ([RecordedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_DispatchExpenses_ReviewedBy] FOREIGN KEY ([ReviewedBy]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_DispatchExpenses_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_DispatchExpenses_Amount] CHECK ([Amount]>0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount]>=0)),
    CONSTRAINT [CK_DispatchExpenses_Approval] CHECK ([ApprovalStatus] IN (N'Pending',N'Approved',N'Rejected')),
    CONSTRAINT [CK_DispatchExpenses_ReviewShape] CHECK (([ApprovalStatus]=N'Pending' AND [ReviewedBy] IS NULL AND [ReviewedAt] IS NULL AND [ApprovedAmount] IS NULL) OR ([ApprovalStatus]<>N'Pending' AND [ReviewedBy] IS NOT NULL AND [ReviewedAt] IS NOT NULL AND [ApprovedAmount] IS NOT NULL))
);
GO
CREATE INDEX [IX_DispatchExpenses_Dispatch_Status] ON [dbo].[DispatchExpenses] ([DispatchId],[ApprovalStatus]);
GO
CREATE UNIQUE INDEX [UX_DispatchExpenses_ReviewIdempotency] ON [dbo].[DispatchExpenses] ([BusinessId],[ReviewIdempotencyKey]) WHERE [ReviewIdempotencyKey] IS NOT NULL;
GO
