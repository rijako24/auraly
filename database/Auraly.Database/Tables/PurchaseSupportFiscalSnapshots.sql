CREATE TABLE [fiscal].[PurchaseSupportFiscalSnapshots]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [Environment] TINYINT NOT NULL,
    [UniqueCode] NVARCHAR(96) NULL,
    [QrPayload] NVARCHAR(2000) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PurchaseSupportFiscalSnapshots] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_PurchaseSupportFiscalSnapshots_FiscalDocuments]
      FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[FiscalDocuments] ([DocumentId]),
    CONSTRAINT [CK_PurchaseSupportFiscalSnapshots_Environment] CHECK ([Environment] IN (1,2)),
    CONSTRAINT [CK_PurchaseSupportFiscalSnapshots_Json] CHECK (ISJSON([SnapshotJson])=1)
);
