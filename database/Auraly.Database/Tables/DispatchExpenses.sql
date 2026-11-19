CREATE TABLE [dbo].[DispatchExpenses] (
    [DispatchExpenseId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DispatchId] UNIQUEIDENTIFIER NOT NULL,
    [Category] NVARCHAR(120) NOT NULL,
    [ExpenseConceptId] UNIQUEIDENTIFIER NULL,
    [AcceptedExpenseId] UNIQUEIDENTIFIER NULL,
    [CashPaymentMethodId] UNIQUEIDENTIFIER NULL,
    [CashPaymentMethodVersion] INT NULL,
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
    CONSTRAINT [FK_DispatchExpenses_Concept] FOREIGN KEY ([ExpenseConceptId]) REFERENCES [dbo].[ExpenseConcepts] ([ExpenseConceptId]),
    CONSTRAINT [FK_DispatchExpenses_AcceptedExpense] FOREIGN KEY ([AcceptedExpenseId]) REFERENCES [dbo].[Expenses] ([ExpenseId]),
    CONSTRAINT [FK_DispatchExpenses_CashForm] FOREIGN KEY ([CashPaymentMethodId]) REFERENCES [reference].[PaymentMethods] ([PaymentMethodId]),
    CONSTRAINT [CK_DispatchExpenses_AcceptedExpense] CHECK ([AcceptedExpenseId] IS NULL OR ([ApprovalStatus]=N'Approved' AND [CashPaymentMethodId] IS NOT NULL AND [CashPaymentMethodVersion] IS NOT NULL AND [CashPaymentMethodVersion]>0)),
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
CREATE UNIQUE INDEX [UX_DispatchExpenses_AcceptedExpense] ON [dbo].[DispatchExpenses] ([AcceptedExpenseId]) WHERE [AcceptedExpenseId] IS NOT NULL;
GO
CREATE INDEX [IX_DispatchExpenses_CashPaymentMethod] ON [dbo].[DispatchExpenses] ([CashPaymentMethodId]) WHERE [CashPaymentMethodId] IS NOT NULL;
GO
