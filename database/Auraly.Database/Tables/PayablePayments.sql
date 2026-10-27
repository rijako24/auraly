CREATE TABLE [dbo].[SupplierPayments]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
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
    -- NULL denotes the native payment's own source. A manual group references
    -- the existing parent source and never owns another job or document number.
    [ParentSourceDocumentId] UNIQUEIDENTIFIER NULL,
    [ParentSourceDocumentType] NVARCHAR(64) NULL,
    [SourceGroupId] UNIQUEIDENTIFIER NULL,
    -- Explicit site of a manual group; native history retains application scope.
    [PartySiteId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[PartySites]([PartySiteId]),
    CONSTRAINT [FK_SupplierPayments_ParentSource] FOREIGN KEY ([ParentSourceDocumentId],[ParentSourceDocumentType])
      REFERENCES [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [CK_SupplierPayments_Origin] CHECK (
      ([ParentSourceDocumentId] IS NULL AND [ParentSourceDocumentType] IS NULL AND [SourceGroupId] IS NULL
        AND [DocumentSeriesId] IS NOT NULL AND [DocumentNumber] IS NOT NULL AND [DocumentPrefix] IS NOT NULL
        AND [DocumentSeriesCode] IS NOT NULL AND [DocumentConsecutive] IS NOT NULL)
      OR ([ParentSourceDocumentId] IS NOT NULL AND [ParentSourceDocumentType] IS NOT NULL AND [SourceGroupId] IS NOT NULL
        AND [SourceGroupId]<>'00000000-0000-0000-0000-000000000000'
        AND [DocumentSeriesId] IS NULL AND [DocumentNumber] IS NULL AND [DocumentPrefix] IS NULL
        AND [DocumentSeriesCode] IS NULL AND [DocumentConsecutive] IS NULL)),
    CONSTRAINT [PK_SupplierPayments] PRIMARY KEY CLUSTERED ([PaymentId]),
    CONSTRAINT [FK_SupplierPayments_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierPayments_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_SupplierPayments_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_SupplierPayments_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_SupplierPayments_Users] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
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

CREATE UNIQUE INDEX [UX_SupplierPayments_NativeNumber]
    ON [dbo].[SupplierPayments]([BusinessId],[DocumentNumber]) WHERE [ParentSourceDocumentId] IS NULL;
GO
CREATE UNIQUE INDEX [UX_SupplierPayments_ParentGroup]
    ON [dbo].[SupplierPayments]([ParentSourceDocumentId],[ParentSourceDocumentType],[SourceGroupId])
    WHERE [ParentSourceDocumentId] IS NOT NULL;
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
    [AccountId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [CK_SupplierPaymentTenders_SourceLine] CHECK (
      ([AccountId] IS NULL AND [SourceLineId] IS NULL) OR
      ([SourceLineId] IS NOT NULL AND [SourceLineId]<>'00000000-0000-0000-0000-000000000000')),
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
    [FinancialDiscountEarned] BIT NULL,
    [FinancialDiscountAdjustment] DECIMAL(19,4) NULL,
    [AppliedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_SupplierPaymentApplications] PRIMARY KEY CLUSTERED ([PaymentId], [LineNumber]),
    CONSTRAINT [FK_SupplierPaymentApplications_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[SupplierPayments] ([PaymentId]),
    CONSTRAINT [FK_SupplierPaymentApplications_Payable] FOREIGN KEY ([PayableId]) REFERENCES [dbo].[Payables] ([PayableId]),
    CONSTRAINT [UQ_SupplierPaymentApplications_Payable] UNIQUE ([PaymentId], [PayableId]),
    CONSTRAINT [CK_SupplierPaymentApplications_Line] CHECK ([LineNumber] > 0),
    CONSTRAINT [CK_SupplierPaymentApplications_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_SupplierPaymentApplications_Discount] CHECK (
      ([FinancialDiscountEarned] IS NULL AND [FinancialDiscountAdjustment] IS NULL) OR
      ([FinancialDiscountEarned] IS NOT NULL AND [FinancialDiscountAdjustment] IS NOT NULL))
);
GO
CREATE UNIQUE INDEX [UX_SupplierPaymentApplications_DiscountDecision]
    ON [dbo].[SupplierPaymentApplications] ([PayableId])
    WHERE [FinancialDiscountEarned] IS NOT NULL;
GO
CREATE INDEX [IX_SupplierPaymentApplications_Pending]
    ON [dbo].[SupplierPaymentApplications] ([PayableId], [AppliedAt])
    INCLUDE ([PaymentId], [Amount], [FinancialDiscountAdjustment]);
GO
