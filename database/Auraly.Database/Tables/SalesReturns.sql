CREATE TABLE [dbo].[SalesReturns]
(
    [ReturnId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [OriginalDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(64) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [ReturnedAt] DATETIMEOFFSET(7) NOT NULL,
    [EconomicResolution] NVARCHAR(24) NOT NULL,
    [RefundMethodCode] NVARCHAR(32) NULL,
    [OriginalPaymentNumber] INT NULL,
    [CorrectionCode] NVARCHAR(4) NOT NULL,
    [ReasonCode] NVARCHAR(32) NOT NULL,
    [ReasonDescription] NVARCHAR(300) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [CustomerIdentification] NVARCHAR(64) NOT NULL,
    [UntaxedAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [FiscalStatus] NVARCHAR(48) NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesReturns] PRIMARY KEY CLUSTERED ([ReturnId]),
    CONSTRAINT [UQ_SalesReturns_Return_Original] UNIQUE ([ReturnId],[OriginalDocumentId]),
    CONSTRAINT [FK_SalesReturns_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesReturns_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_SalesReturns_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_SalesReturns_OriginalDocument] FOREIGN KEY ([OriginalDocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_SalesReturns_OriginalPayment] FOREIGN KEY ([OriginalDocumentId],[OriginalPaymentNumber]) REFERENCES [dbo].[SalesPayments] ([DocumentId],[PaymentNumber]),
    CONSTRAINT [FK_SalesReturns_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_SalesReturns_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_SalesReturns_CreatedBy] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_SalesReturns_Business_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [UQ_SalesReturns_Number] UNIQUE ([BusinessId],[DocumentPrefix],[DocumentSeriesCode],[DocumentConsecutive]),
    CONSTRAINT [CK_SalesReturns_Resolution] CHECK
      (([EconomicResolution]=N'Refund' AND [RefundMethodCode]=N'Cash'
          AND [OriginalPaymentNumber] IS NOT NULL AND [WorkSessionId] IS NOT NULL) OR
       ([EconomicResolution]=N'CustomerCredit' AND [RefundMethodCode] IS NULL
          AND [OriginalPaymentNumber] IS NULL)),
    CONSTRAINT [CK_SalesReturns_Correction] CHECK ([CorrectionCode]=N'1'),
    CONSTRAINT [CK_SalesReturns_Amounts] CHECK
      ([UntaxedAmount]>=0 AND [TaxAmount]>=0 AND [TotalAmount]>0),
    CONSTRAINT [CK_SalesReturns_Status] CHECK ([Status] IN (N'Accepted',N'Processed'))
);
GO
CREATE INDEX [IX_SalesReturns_Original_Status]
  ON [dbo].[SalesReturns] ([OriginalDocumentId],[Status],[AcceptedAt]);
GO
CREATE INDEX [IX_SalesReturns_Business_Returned]
  ON [dbo].[SalesReturns] ([BusinessId],[ReturnedAt],[ReturnId]);
GO

CREATE TABLE [dbo].[SalesReturnLines]
(
    [ReturnId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [OriginalLineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [DescriptionSnapshot] NVARCHAR(300) NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [UnitPrice] DECIMAL(19,4) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(16) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [UntaxedAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    [RecognizedUnitCost] DECIMAL(19,6) NOT NULL,
    [InventoryDisposition] NVARCHAR(24) NOT NULL,
    CONSTRAINT [PK_SalesReturnLines] PRIMARY KEY CLUSTERED ([ReturnId],[LineNumber]),
    CONSTRAINT [UQ_SalesReturnLines_OriginalLine] UNIQUE ([ReturnId],[OriginalLineNumber]),
    CONSTRAINT [FK_SalesReturnLines_Return] FOREIGN KEY ([ReturnId],[OriginalDocumentId])
      REFERENCES [dbo].[SalesReturns] ([ReturnId],[OriginalDocumentId]),
    CONSTRAINT [FK_SalesReturnLines_OriginalLine] FOREIGN KEY ([OriginalDocumentId],[OriginalLineNumber])
      REFERENCES [dbo].[SalesDocumentLines] ([DocumentId],[LineNumber]),
    CONSTRAINT [FK_SalesReturnLines_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_SalesReturnLines_Values] CHECK
      ([LineNumber]>0 AND [OriginalLineNumber]>0 AND [Quantity]>0 AND [UnitPrice]>=0 AND
       [DiscountAmount]>=0 AND [TaxRate]>=0 AND [UntaxedAmount]>=0 AND [TaxAmount]>=0 AND
       [LineTotal]>0 AND [RecognizedUnitCost]>=0),
    CONSTRAINT [CK_SalesReturnLines_Disposition] CHECK
      ([InventoryDisposition] IN (N'Sellable',N'Inspection',N'Damaged',N'NotReturned'))
);
GO
CREATE INDEX [IX_SalesReturnLines_Original]
  ON [dbo].[SalesReturnLines] ([OriginalDocumentId],[OriginalLineNumber])
  INCLUDE ([Quantity],[DiscountAmount],[UntaxedAmount],[TaxAmount],[LineTotal]);
GO

CREATE TABLE [dbo].[SalesReturnSettlements]
(
    [ReturnId] UNIQUEIDENTIFIER NOT NULL,
    [SettlementNumber] INT NOT NULL,
    [SettlementType] NVARCHAR(24) NOT NULL,
    [MethodCode] NVARCHAR(32) NULL,
    [OriginalDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalPaymentNumber] INT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [Reference] NVARCHAR(160) NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesReturnSettlements] PRIMARY KEY CLUSTERED ([ReturnId],[SettlementNumber]),
    CONSTRAINT [FK_SalesReturnSettlements_Return] FOREIGN KEY ([ReturnId]) REFERENCES [dbo].[SalesReturns] ([ReturnId]),
    CONSTRAINT [FK_SalesReturnSettlements_ReturnOriginal] FOREIGN KEY ([ReturnId],[OriginalDocumentId])
      REFERENCES [dbo].[SalesReturns] ([ReturnId],[OriginalDocumentId]),
    CONSTRAINT [FK_SalesReturnSettlements_OriginalPayment] FOREIGN KEY ([OriginalDocumentId],[OriginalPaymentNumber])
      REFERENCES [dbo].[SalesPayments] ([DocumentId],[PaymentNumber]),
    CONSTRAINT [CK_SalesReturnSettlements_Type] CHECK
      (([SettlementType]=N'Refund' AND [MethodCode]=N'Cash' AND [OriginalPaymentNumber] IS NOT NULL) OR
       ([SettlementType]=N'CustomerCredit' AND [MethodCode] IS NULL AND [OriginalPaymentNumber] IS NULL)),
    CONSTRAINT [CK_SalesReturnSettlements_Amount] CHECK ([Amount]>0)
);
GO

CREATE TABLE [dbo].[CustomerCredits]
(
    [CustomerCreditId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [SourceReturnId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalAmount] DECIMAL(19,4) NOT NULL,
    [AvailableAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CustomerCredits] PRIMARY KEY CLUSTERED ([CustomerCreditId]),
    CONSTRAINT [FK_CustomerCredits_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CustomerCredits_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_CustomerCredits_Return] FOREIGN KEY ([SourceReturnId]) REFERENCES [dbo].[SalesReturns] ([ReturnId]),
    CONSTRAINT [UQ_CustomerCredits_SourceReturn] UNIQUE ([SourceReturnId]),
    CONSTRAINT [CK_CustomerCredits_Amounts] CHECK
      ([OriginalAmount]>0 AND [AvailableAmount]>=0 AND [AvailableAmount]<=[OriginalAmount]),
    CONSTRAINT [CK_CustomerCredits_Status] CHECK ([Status] IN (N'Open',N'Applied',N'Exhausted'))
);
GO
CREATE INDEX [IX_CustomerCredits_Business_Customer_Status]
  ON [dbo].[CustomerCredits] ([BusinessId],[CustomerId],[Status]);
GO
