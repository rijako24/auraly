CREATE TABLE [dbo].[CustomerCreditProfiles]
(
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CreditLimit] DECIMAL(19,4) NULL,
    [DefaultDueDays] INT NOT NULL DEFAULT 0,
    [IsCreditEnabled] BIT NOT NULL DEFAULT 0,
    [UpdatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CustomerCreditProfiles] PRIMARY KEY ([CustomerId]),
    CONSTRAINT [FK_CustomerCreditProfiles_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_CustomerCreditProfiles_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CustomerCreditProfiles_Users] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_CustomerCreditProfiles_Limit] CHECK ([CreditLimit] IS NULL OR [CreditLimit] >= 0),
    CONSTRAINT [CK_CustomerCreditProfiles_DueDays] CHECK ([DefaultDueDays] BETWEEN 0 AND 3650)
);
GO
CREATE UNIQUE INDEX [UX_CustomerCreditProfiles_Business_Customer] ON [dbo].[CustomerCreditProfiles] ([BusinessId],[CustomerId]);
GO

CREATE TABLE [dbo].[Receivables]
(
    [ReceivableId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [DocumentNumber] NVARCHAR(64) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [OriginalAmount] DECIMAL(19,4) NOT NULL,
    [OutstandingAmount] DECIMAL(19,4) NOT NULL,
    [DueDate] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Receivables] PRIMARY KEY ([ReceivableId]),
    CONSTRAINT [FK_Receivables_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_Receivables_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_Receivables_SourceJob] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType]) REFERENCES [dbo].[DocumentProcessingJobs] ([DocumentId],[DocumentType]),
    CONSTRAINT [UQ_Receivables_Source] UNIQUE ([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [CK_Receivables_Amounts] CHECK ([OriginalAmount] > 0 AND [OutstandingAmount] BETWEEN 0 AND [OriginalAmount]),
    CONSTRAINT [CK_Receivables_Currency] CHECK ([CurrencyCode] = 'COP'),
    CONSTRAINT [CK_Receivables_Status] CHECK ([Status] IN (N'Open',N'PartiallyPaid',N'Paid',N'Cancelled'))
);
GO
CREATE INDEX [IX_Receivables_Business_Due] ON [dbo].[Receivables] ([BusinessId],[Status],[DueDate]) INCLUDE ([CustomerId],[DocumentNumber],[OutstandingAmount]);
GO
CREATE INDEX [IX_Receivables_Customer_Status] ON [dbo].[Receivables] ([CustomerId],[Status]) INCLUDE ([OutstandingAmount],[DueDate]);
GO

CREATE TABLE [dbo].[ReceivableTransactions]
(
    [ReceivableTransactionId] UNIQUEIDENTIFIER NOT NULL,
    [ReceivableId] UNIQUEIDENTIFIER NOT NULL,
    [TransactionType] NVARCHAR(24) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ReceivableTransactions] PRIMARY KEY ([ReceivableTransactionId]),
    CONSTRAINT [FK_ReceivableTransactions_Receivables] FOREIGN KEY ([ReceivableId]) REFERENCES [dbo].[Receivables] ([ReceivableId]),
    CONSTRAINT [UQ_ReceivableTransactions_Source] UNIQUE ([ReceivableId],[TransactionType],[SourceDocumentId]),
    CONSTRAINT [CK_ReceivableTransactions_Type] CHECK ([TransactionType] IN (N'Opening',N'Payment',N'Reversal',N'Adjustment')),
    CONSTRAINT [CK_ReceivableTransactions_Amount] CHECK ([Amount] <> 0)
);
GO

CREATE TABLE [dbo].[CustomerPayments]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(40) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [PaidAt] DATETIMEOFFSET(7) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [PaymentMethod] NVARCHAR(24) NOT NULL,
    [Reference] NVARCHAR(120) NULL,
    [Notes] NVARCHAR(1000) NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CustomerPayments] PRIMARY KEY ([PaymentId]),
    CONSTRAINT [FK_CustomerPayments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CustomerPayments_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_CustomerPayments_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_CustomerPayments_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_CustomerPayments_Users] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_CustomerPayments_Business_Number] UNIQUE ([BusinessId],[DocumentNumber]),
    CONSTRAINT [UQ_CustomerPayments_Business_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_CustomerPayments_Total] CHECK ([TotalAmount] > 0),
    CONSTRAINT [CK_CustomerPayments_Currency] CHECK ([CurrencyCode] = 'COP'),
    CONSTRAINT [CK_CustomerPayments_Method] CHECK ([PaymentMethod] IN (N'Cash',N'BankTransfer',N'DebitCard',N'CreditCard')),
    CONSTRAINT [CK_CustomerPayments_Status] CHECK ([Status] IN (N'Accepted',N'Processed'))
);
GO
CREATE INDEX [IX_CustomerPayments_Business_Paid] ON [dbo].[CustomerPayments] ([BusinessId],[PaidAt] DESC) INCLUDE ([CustomerId],[Status],[TotalAmount]);
GO

CREATE TABLE [dbo].[CustomerPaymentApplications]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [ReceivableId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [AppliedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_CustomerPaymentApplications] PRIMARY KEY ([PaymentId],[LineNumber]),
    CONSTRAINT [FK_CustomerPaymentApplications_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[CustomerPayments] ([PaymentId]),
    CONSTRAINT [FK_CustomerPaymentApplications_Receivable] FOREIGN KEY ([ReceivableId]) REFERENCES [dbo].[Receivables] ([ReceivableId]),
    CONSTRAINT [UQ_CustomerPaymentApplications_Receivable] UNIQUE ([PaymentId],[ReceivableId]),
    CONSTRAINT [CK_CustomerPaymentApplications_Line] CHECK ([LineNumber] > 0),
    CONSTRAINT [CK_CustomerPaymentApplications_Amount] CHECK ([Amount] > 0)
);
GO
