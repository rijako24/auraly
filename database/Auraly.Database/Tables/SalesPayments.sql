CREATE TABLE [dbo].[SalesPayments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentNumber] INT NOT NULL,
    [MethodCode] NVARCHAR(32) NOT NULL,
    [Amount] DECIMAL(19, 4) NOT NULL,
    [Reference] NVARCHAR(160) NULL,
    [RegisteredAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesPayments] PRIMARY KEY CLUSTERED ([DocumentId], [PaymentNumber]),
    CONSTRAINT [FK_SalesPayments_SalesDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [CK_SalesPayments_Amount] CHECK ([PaymentNumber] > 0 AND [Amount] > 0)
);

