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
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [PartySiteId] UNIQUEIDENTIFIER NULL,
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
    [OpeningEntryId] UNIQUEIDENTIFIER NULL,
    [OpeningEntryLineNumber] INT NULL,
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_Receivables] PRIMARY KEY ([ReceivableId]),
    CONSTRAINT [FK_Receivables_OpeningEntryLine] FOREIGN KEY ([OpeningEntryId],[OpeningEntryLineNumber]) REFERENCES [dbo].[AccountingEntryLines] ([EntryId],[LineNumber]),
    CONSTRAINT [CK_Receivables_OpeningEntryLine] CHECK (([OpeningEntryId] IS NULL AND [OpeningEntryLineNumber] IS NULL) OR ([OpeningEntryId] IS NOT NULL AND [OpeningEntryLineNumber] IS NOT NULL)),
    CONSTRAINT [FK_Receivables_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_Receivables_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_Receivables_Parties] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId]),
    CONSTRAINT [FK_Receivables_PartySites] FOREIGN KEY ([PartySiteId]) REFERENCES [dbo].[PartySites] ([PartySiteId]),
    CONSTRAINT [FK_Receivables_SourceJob] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType]) REFERENCES [dbo].[AccountingPostingJobs] ([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [UQ_Receivables_Source] UNIQUE ([SourceDocumentId],[SourceDocumentType],[SourceLineId]),
    CONSTRAINT [CK_Receivables_SourceLine] CHECK ([SourceLineId] IS NULL OR
      ([SourceLineId]<>'00000000-0000-0000-0000-000000000000' AND [SourceDocumentType]=N'ManualAccountingVoucher')),
    CONSTRAINT [CK_Receivables_Amounts] CHECK ([OriginalAmount] > 0 AND [OutstandingAmount] >= 0),
    CONSTRAINT [CK_Receivables_Debtor] CHECK (([CustomerId] IS NOT NULL AND [PartyId] IS NULL AND [PartySiteId] IS NOT NULL)
        OR ([CustomerId] IS NULL AND [PartyId] IS NOT NULL AND [PartySiteId] IS NULL)),
    CONSTRAINT [CK_Receivables_Currency] CHECK ([CurrencyCode] = 'COP'),
    CONSTRAINT [CK_Receivables_Status] CHECK ([Status] IN (N'Open',N'PartiallyPaid',N'Paid',N'Cancelled'))
);
GO
CREATE INDEX [IX_Receivables_Business_Due] ON [dbo].[Receivables] ([BusinessId],[Status],[DueDate]) INCLUDE ([CustomerId],[DocumentNumber],[OutstandingAmount]);
GO
CREATE INDEX [IX_Receivables_Customer_Status] ON [dbo].[Receivables] ([CustomerId],[Status]) INCLUDE ([OutstandingAmount],[DueDate]);
GO
CREATE INDEX [IX_Receivables_Party_Status] ON [dbo].[Receivables] ([PartyId],[Status]) INCLUDE ([OutstandingAmount],[DueDate])
    WHERE [PartyId] IS NOT NULL;
GO
CREATE INDEX [IX_Receivables_Business_Customer_Site_Status]
    ON [dbo].[Receivables] ([BusinessId],[CustomerId],[PartySiteId],[Status],[DueDate])
    INCLUDE ([DocumentNumber],[OutstandingAmount]);
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
    [SourceDocumentType] NVARCHAR(64) NULL,
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_ReceivableTransactions] PRIMARY KEY ([ReceivableTransactionId]),
    [ReversesTransactionId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [FK_ReceivableTransactions_Reversal] FOREIGN KEY ([ReversesTransactionId])
      REFERENCES [dbo].[ReceivableTransactions]([ReceivableTransactionId]),
    CONSTRAINT [CK_ReceivableTransactions_Reversal] CHECK (
      ([TransactionType] NOT IN(N'Unapplication',N'CreditUnapplication') AND [ReversesTransactionId] IS NULL) OR
      ([TransactionType] IN(N'Unapplication',N'CreditUnapplication') AND [ReversesTransactionId] IS NOT NULL AND [Amount]>0 AND [SourceDocumentType]=N'ManualAccountingVoucher' AND [SourceLineId] IS NOT NULL)),
    CONSTRAINT [FK_ReceivableTransactions_Receivables] FOREIGN KEY ([ReceivableId]) REFERENCES [dbo].[Receivables] ([ReceivableId]),
    CONSTRAINT [FK_ReceivableTransactions_TypedSource] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType])
      REFERENCES [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [UQ_ReceivableTransactions_Source] UNIQUE ([ReceivableId],[TransactionType],[SourceDocumentId],[SourceLineId]),
    CONSTRAINT [CK_ReceivableTransactions_Line] CHECK ([SourceLineId] IS NULL OR
      ([SourceLineId]<>'00000000-0000-0000-0000-000000000000' AND [SourceDocumentType] IS NOT NULL AND [SourceDocumentType]=N'ManualAccountingVoucher')),
    CONSTRAINT [CK_ReceivableTransactions_Type] CHECK ([TransactionType] IN (N'Opening',N'Payment',N'Reversal',N'Adjustment',N'CreditApplication',N'Unapplication',N'CreditUnapplication')),
    CONSTRAINT [CK_ReceivableTransactions_Amount] CHECK ([Amount] <> 0)
);
GO
CREATE INDEX [IX_ReceivableTransactions_Unapplications] ON [dbo].[ReceivableTransactions]([ReceivableId],[OccurredAt])
    INCLUDE([Amount],[ReversesTransactionId],[CreatedAt]) WHERE [TransactionType]=N'Unapplication';
GO

CREATE TABLE [dbo].[CustomerPayments]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NULL,
    [DocumentNumber] NVARCHAR(40) NULL,
    [DocumentPrefix] NVARCHAR(8) NULL,
    [DocumentSeriesCode] NVARCHAR(16) NULL,
    [DocumentConsecutive] BIGINT NULL,
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
    [PartyId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[Parties]([PartyId]),
    -- NULL also permits native collections spanning several invoice sites.
    [PartySiteId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[PartySites]([PartySiteId]),
    -- NULL denotes the native payment's own source. A manual group references
    -- the existing parent source and never owns another job or document number.
    [ParentSourceDocumentId] UNIQUEIDENTIFIER NULL,
    [ParentSourceDocumentType] NVARCHAR(64) NULL,
    [SourceGroupId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [FK_CustomerPayments_ParentSource] FOREIGN KEY ([ParentSourceDocumentId],[ParentSourceDocumentType])
      REFERENCES [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [CK_CustomerPayments_Party] CHECK (
      ([CustomerId] IS NOT NULL AND [PartyId] IS NULL) OR
      ([CustomerId] IS NULL AND [PartyId] IS NOT NULL AND [ParentSourceDocumentId] IS NOT NULL)),
    CONSTRAINT [CK_CustomerPayments_Origin] CHECK (
      ([ParentSourceDocumentId] IS NULL AND [ParentSourceDocumentType] IS NULL AND [SourceGroupId] IS NULL
        AND [DocumentSeriesId] IS NOT NULL AND [DocumentNumber] IS NOT NULL AND [DocumentPrefix] IS NOT NULL
        AND [DocumentSeriesCode] IS NOT NULL AND [DocumentConsecutive] IS NOT NULL)
      OR ([ParentSourceDocumentId] IS NOT NULL AND [ParentSourceDocumentType] IS NOT NULL AND [SourceGroupId] IS NOT NULL
        AND [SourceGroupId]<>'00000000-0000-0000-0000-000000000000'
        AND [DocumentSeriesId] IS NULL AND [DocumentNumber] IS NULL AND [DocumentPrefix] IS NULL
        AND [DocumentSeriesCode] IS NULL AND [DocumentConsecutive] IS NULL)),
    CONSTRAINT [PK_CustomerPayments] PRIMARY KEY ([PaymentId]),
    CONSTRAINT [FK_CustomerPayments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CustomerPayments_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_CustomerPayments_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_CustomerPayments_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_CustomerPayments_Users] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_CustomerPayments_Business_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_CustomerPayments_Total] CHECK ([TotalAmount] > 0),
    CONSTRAINT [CK_CustomerPayments_Currency] CHECK ([CurrencyCode] = 'COP'),
    CONSTRAINT [CK_CustomerPayments_Status] CHECK ([Status] IN (N'Accepted',N'Processed'))
);
GO
CREATE INDEX [IX_CustomerPayments_Business_Paid] ON [dbo].[CustomerPayments] ([BusinessId],[PaidAt] DESC) INCLUDE ([CustomerId],[Status],[TotalAmount]);
GO

CREATE UNIQUE INDEX [UX_CustomerPayments_NativeNumber]
    ON [dbo].[CustomerPayments]([BusinessId],[DocumentNumber]) WHERE [ParentSourceDocumentId] IS NULL;
GO
CREATE UNIQUE INDEX [UX_CustomerPayments_ParentGroup]
    ON [dbo].[CustomerPayments]([ParentSourceDocumentId],[ParentSourceDocumentType],[SourceGroupId])
    WHERE [ParentSourceDocumentId] IS NOT NULL;
GO

CREATE TABLE [dbo].[CustomerPaymentTenders]
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
    [AccountId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [CK_CustomerPaymentTenders_SourceLine] CHECK (
      ([AccountId] IS NULL AND [SourceLineId] IS NULL) OR
      ([SourceLineId] IS NOT NULL AND [SourceLineId]<>'00000000-0000-0000-0000-000000000000')),
    CONSTRAINT [PK_CustomerPaymentTenders] PRIMARY KEY CLUSTERED ([PaymentId],[LineNumber]),
    CONSTRAINT [FK_CustomerPaymentTenders_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[CustomerPayments] ([PaymentId]),
    CONSTRAINT [FK_CustomerPaymentTenders_BankAccount] FOREIGN KEY ([BankAccountId]) REFERENCES [accounting].[BankAccounts] ([BankAccountId]),
    CONSTRAINT [CK_CustomerPaymentTenders_Amount] CHECK ([LineNumber]>0 AND [Amount]>0),
    CONSTRAINT [CK_CustomerPaymentTenders_Cash] CHECK ([TenderedAmount] IS NULL OR ([MethodCode]=N'Cash' AND [TenderedAmount]>=[Amount]))
);
GO
CREATE INDEX [IX_CustomerPaymentTenders_BankAccount] ON [dbo].[CustomerPaymentTenders] ([BankAccountId]) WHERE [BankAccountId] IS NOT NULL;
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
CREATE INDEX [IX_CustomerPaymentApplications_Receivable_Applied]
    ON [dbo].[CustomerPaymentApplications] ([ReceivableId], [AppliedAt])
    INCLUDE ([PaymentId], [Amount]);
GO
