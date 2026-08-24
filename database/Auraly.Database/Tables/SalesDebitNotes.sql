CREATE TABLE [dbo].[SalesDebitNotes]
(
    [DebitNoteId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(64) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [DueAt] DATETIMEOFFSET(7) NOT NULL,
    [ConceptCode] NVARCHAR(4) NOT NULL,
    [ReasonDescription] NVARCHAR(300) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
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
    CONSTRAINT [PK_SalesDebitNotes] PRIMARY KEY CLUSTERED ([DebitNoteId]),
    CONSTRAINT [FK_SalesDebitNotes_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesDebitNotes_OriginalDocument] FOREIGN KEY ([OriginalDocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_SalesDebitNotes_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_SalesDebitNotes_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_SalesDebitNotes_CreatedBy] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_SalesDebitNotes_Business_Idempotency] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [UQ_SalesDebitNotes_Number] UNIQUE ([BusinessId],[DocumentPrefix],[DocumentSeriesCode],[DocumentConsecutive]),
    CONSTRAINT [CK_SalesDebitNotes_Concept] CHECK ([ConceptCode] IN (N'1',N'2',N'3',N'4')),
    CONSTRAINT [CK_SalesDebitNotes_Dates] CHECK ([DueAt]>=[IssuedAt]),
    CONSTRAINT [CK_SalesDebitNotes_Amounts] CHECK
      ([UntaxedAmount]>0 AND [TaxAmount]>=0 AND [TotalAmount]=[UntaxedAmount]+[TaxAmount]),
    CONSTRAINT [CK_SalesDebitNotes_Status] CHECK ([Status] IN (N'Accepted',N'Processed'))
);
GO
CREATE INDEX [IX_SalesDebitNotes_Business_Issued]
  ON [dbo].[SalesDebitNotes] ([BusinessId],[IssuedAt],[DebitNoteId]);
GO
CREATE INDEX [IX_SalesDebitNotes_Original]
  ON [dbo].[SalesDebitNotes] ([OriginalDocumentId],[IssuedAt]);
GO

CREATE TABLE [dbo].[SalesDebitNoteLines]
(
    [DebitNoteId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [DescriptionSnapshot] NVARCHAR(300) NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [UnitPrice] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(16) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [UntaxedAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_SalesDebitNoteLines] PRIMARY KEY CLUSTERED ([DebitNoteId],[LineNumber]),
    CONSTRAINT [FK_SalesDebitNoteLines_Note] FOREIGN KEY ([DebitNoteId]) REFERENCES [dbo].[SalesDebitNotes] ([DebitNoteId]),
    CONSTRAINT [CK_SalesDebitNoteLines_Values] CHECK
      ([LineNumber]>0 AND [Quantity]>0 AND [UnitPrice]>0 AND [TaxRate]>=0 AND
       [UntaxedAmount]>0 AND [TaxAmount]>=0 AND [LineTotal]=[UntaxedAmount]+[TaxAmount])
);
GO

CREATE TABLE [dbo].[SalesDebitNoteFiscalSnapshots]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [UniqueCode] NVARCHAR(96) NULL,
    [QrPayload] NVARCHAR(2048) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesDebitNoteFiscalSnapshots] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_SalesDebitNoteFiscalSnapshots_Notes]
      FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDebitNotes] ([DebitNoteId]),
    CONSTRAINT [CK_SalesDebitNoteFiscalSnapshots_Environment] CHECK ([Environment] IN (1,2)),
    CONSTRAINT [CK_SalesDebitNoteFiscalSnapshots_Json] CHECK (ISJSON([SnapshotJson])=1)
);
GO
