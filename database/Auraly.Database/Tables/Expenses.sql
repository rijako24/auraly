CREATE TABLE [dbo].[ExpenseConcepts]
(
    [ExpenseConceptId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [ExpenseAccountId] UNIQUEIDENTIFIER NOT NULL,
    [DefaultCostCenterId] UNIQUEIDENTIFIER NULL,
    [WithholdingConceptCode] NVARCHAR(32) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_ExpenseConcepts] PRIMARY KEY ([ExpenseConceptId]),
    CONSTRAINT [UQ_ExpenseConcepts_Business_Code] UNIQUE ([BusinessId],[Code]),
    CONSTRAINT [UQ_ExpenseConcepts_Business_Concept] UNIQUE ([BusinessId],[ExpenseConceptId]),
    CONSTRAINT [FK_ExpenseConcepts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_ExpenseConcepts_Accounts] FOREIGN KEY ([ExpenseAccountId]) REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    CONSTRAINT [FK_ExpenseConcepts_CostCenters] FOREIGN KEY ([BusinessId],[DefaultCostCenterId]) REFERENCES [dbo].[AccountingCostCenters]([BusinessId],[CostCenterId])
);
GO

CREATE TABLE [dbo].[Expenses]
(
    [ExpenseId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [ExpenseConceptId] UNIQUEIDENTIFIER NOT NULL,
    [CostCenterId] UNIQUEIDENTIFIER NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(64) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(8) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [SupplierDocumentNumber] NVARCHAR(80) NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [DueDate] DATETIMEOFFSET(7) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [Description] NVARCHAR(300) NOT NULL,
    [TaxExclusiveAmount] DECIMAL(19,4) NOT NULL,
    [VatAmount] DECIMAL(19,4) NOT NULL,
    [GrossAmount] DECIMAL(19,4) NOT NULL,
    [WithholdingAmount] DECIMAL(19,4) NOT NULL,
    [NetPayable] DECIMAL(19,4) NOT NULL,
    [EvidenceUrl] NVARCHAR(1000) NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [RequestHash] BINARY(32) NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Expenses] PRIMARY KEY ([ExpenseId]),
    CONSTRAINT [UQ_Expenses_Business_Number] UNIQUE ([BusinessId],[DocumentNumber]),
    CONSTRAINT [UQ_Expenses_Business_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [UQ_Expenses_Supplier_Document] UNIQUE ([BusinessId],[SupplierId],[SupplierDocumentNumber]),
    CONSTRAINT [FK_Expenses_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_Expenses_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers]([SupplierId]),
    CONSTRAINT [FK_Expenses_Concepts] FOREIGN KEY ([BusinessId],[ExpenseConceptId]) REFERENCES [dbo].[ExpenseConcepts]([BusinessId],[ExpenseConceptId]),
    CONSTRAINT [FK_Expenses_CostCenters] FOREIGN KEY ([BusinessId],[CostCenterId]) REFERENCES [dbo].[AccountingCostCenters]([BusinessId],[CostCenterId]),
    CONSTRAINT [FK_Expenses_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries]([DocumentSeriesId]),
    CONSTRAINT [FK_Expenses_Users] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_Expenses_Amounts] CHECK ([TaxExclusiveAmount]>0 AND [VatAmount]>=0 AND [GrossAmount]=[TaxExclusiveAmount]+[VatAmount] AND [NetPayable]=[GrossAmount]-[WithholdingAmount] AND [NetPayable]>=0),
    CONSTRAINT [CK_Expenses_Dates] CHECK ([DueDate]>=[IssuedAt]),
    CONSTRAINT [CK_Expenses_Status] CHECK ([Status] IN (N'Accepted',N'Processed'))
);
GO
CREATE INDEX [IX_Expenses_Business_Issued] ON [dbo].[Expenses]([BusinessId],[IssuedAt] DESC);
GO
