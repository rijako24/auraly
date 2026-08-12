CREATE TABLE [dbo].[SalesReturnFiscalSnapshots]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [UniqueCode] NVARCHAR(96) NULL,
    [QrPayload] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesReturnFiscalSnapshots] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_SalesReturnFiscalSnapshots_FiscalDocuments]
      FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[FiscalDocuments] ([DocumentId]),
    CONSTRAINT [CK_SalesReturnFiscalSnapshots_Environment] CHECK ([Environment] IN (1,2))
);
