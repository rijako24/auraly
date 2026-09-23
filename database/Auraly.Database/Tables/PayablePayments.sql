CREATE TABLE [dbo].[SupplierPayments]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(40) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [PaidAt] DATETIMEOFFSET(7) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SupplierPayments] PRIMARY KEY CLUSTERED ([PaymentId]),
    CONSTRAINT [FK_SupplierPayments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierPayments_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_SupplierPayments_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_SupplierPayments_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_SupplierPayments_Users] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_SupplierPayments_Business_Number] UNIQUE ([BusinessId], [DocumentNumber]),
    CONSTRAINT [UQ_SupplierPayments_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [CK_SupplierPayments_Total] CHECK ([TotalAmount] > 0),
    CONSTRAINT [CK_SupplierPayments_Currency] CHECK ([CurrencyCode] = 'COP'),
    CONSTRAINT [CK_SupplierPayments_Status] CHECK ([Status] IN (N'Accepted', N'Processed'))
);
GO
CREATE INDEX [IX_SupplierPayments_Business_Paid]
    ON [dbo].[SupplierPayments] ([BusinessId], [PaidAt] DESC)
    INCLUDE ([SupplierId], [Status], [TotalAmount]);
GO

CREATE TABLE [dbo].[SupplierPaymentTenders]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [MethodCode] NVARCHAR(32) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [TenderedAmount] DECIMAL(19,4) NULL,
    [BankAccountId] UNIQUEIDENTIFIER NULL,
    [Reference] NVARCHAR(160) NULL,
    [Notes] NVARCHAR(500) NULL,
    [CardFranchiseCode] NVARCHAR(64) NULL,
    [ApprovalNumber] NVARCHAR(100) NULL,
    CONSTRAINT [PK_SupplierPaymentTenders] PRIMARY KEY CLUSTERED ([PaymentId],[LineNumber]),
    CONSTRAINT [FK_SupplierPaymentTenders_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[SupplierPayments] ([PaymentId]),
    CONSTRAINT [FK_SupplierPaymentTenders_BankAccount] FOREIGN KEY ([BankAccountId]) REFERENCES [accounting].[BankAccounts] ([BankAccountId]),
    CONSTRAINT [CK_SupplierPaymentTenders_Amount] CHECK ([LineNumber]>0 AND [Amount]>0),
    CONSTRAINT [CK_SupplierPaymentTenders_Cash] CHECK ([TenderedAmount] IS NULL OR ([MethodCode]=N'Cash' AND [TenderedAmount]>=[Amount]))
);
GO
CREATE INDEX [IX_SupplierPaymentTenders_BankAccount] ON [dbo].[SupplierPaymentTenders] ([BankAccountId]) WHERE [BankAccountId] IS NOT NULL;
GO

CREATE TABLE [dbo].[SupplierPaymentApplications]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [PayableId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [AppliedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_SupplierPaymentApplications] PRIMARY KEY CLUSTERED ([PaymentId], [LineNumber]),
    CONSTRAINT [FK_SupplierPaymentApplications_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[SupplierPayments] ([PaymentId]),
    CONSTRAINT [FK_SupplierPaymentApplications_Payable] FOREIGN KEY ([PayableId]) REFERENCES [dbo].[Payables] ([PayableId]),
    CONSTRAINT [UQ_SupplierPaymentApplications_Payable] UNIQUE ([PaymentId], [PayableId]),
    CONSTRAINT [CK_SupplierPaymentApplications_Line] CHECK ([LineNumber] > 0),
    CONSTRAINT [CK_SupplierPaymentApplications_Amount] CHECK ([Amount] > 0)
);
GO
CREATE INDEX [IX_SupplierPaymentApplications_Pending]
    ON [dbo].[SupplierPaymentApplications] ([PayableId], [AppliedAt])
    INCLUDE ([PaymentId], [Amount]);
GO
