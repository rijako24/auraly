CREATE TABLE [dbo].[SalesPayments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentNumber] INT NOT NULL,
    [MethodCode] NVARCHAR(32) NOT NULL,
    [Amount] DECIMAL(19, 4) NOT NULL,
    [Reference] NVARCHAR(160) NULL,
    [Notes] NVARCHAR(500) NULL,
    [CardFranchiseCode] NVARCHAR(64) NULL,
    [ApprovalNumber] NVARCHAR(100) NULL,
    [BankAccountId] UNIQUEIDENTIFIER NULL,
    [RegisteredAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesPayments] PRIMARY KEY CLUSTERED ([DocumentId], [PaymentNumber]),
    CONSTRAINT [FK_SalesPayments_SalesDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_SalesPayments_BankAccount] FOREIGN KEY ([BankAccountId]) REFERENCES [accounting].[BankAccounts] ([BankAccountId]),
    CONSTRAINT [CK_SalesPayments_Amount] CHECK ([PaymentNumber] > 0 AND [Amount] > 0),
    CONSTRAINT [CK_SalesPayments_CardData] CHECK (
        ([CardFranchiseCode] IS NULL AND [ApprovalNumber] IS NULL) OR
        ([MethodCode] IN (N'Card',N'DebitCard',N'CreditCard') AND
         NULLIF(LTRIM(RTRIM([CardFranchiseCode])),N'') IS NOT NULL AND
         NULLIF(LTRIM(RTRIM([ApprovalNumber])),N'') IS NOT NULL)),
    -- BankAccountId is populated for transfers when accounting is active.
    -- It remains null for tenants operating without the accounting module.
    CONSTRAINT [CK_SalesPayments_BankAccount] CHECK
      ([MethodCode]=N'Transfer' OR [BankAccountId] IS NULL),
    CONSTRAINT [CK_SalesPayments_TransferEvidence] CHECK
      ([MethodCode]=N'Transfer' OR [Notes] IS NULL)
);

