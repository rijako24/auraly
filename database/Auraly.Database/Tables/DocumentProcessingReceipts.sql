CREATE TABLE [dbo].[DocumentProcessingReceipts]
(
    [ReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(64) NOT NULL,
    [Status] NVARCHAR(32) NOT NULL,
    [AttemptCount] INT NOT NULL,
    [AcquiredAt] DATETIMEOFFSET(7) NOT NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [LastError] NVARCHAR(2000) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_DocumentProcessingReceipts] PRIMARY KEY CLUSTERED ([ReceiptId]),
    CONSTRAINT [FK_DocumentProcessingReceipts_SalesDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [UQ_DocumentProcessingReceipts_Document] UNIQUE ([DocumentId], [DocumentType]),
    CONSTRAINT [CK_DocumentProcessingReceipts_Attempts] CHECK ([AttemptCount] > 0)
);

GO

CREATE INDEX [IX_DocumentProcessingReceipts_Status_Acquired]
    ON [dbo].[DocumentProcessingReceipts] ([Status], [AcquiredAt]);

