CREATE TABLE [dbo].[FiscalDocuments]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(32) NOT NULL,
    [FiscalDocumentType] NVARCHAR(24) NOT NULL,
    [AuralyDocumentNumber] NVARCHAR(64) NOT NULL,
    [FiscalNumber] NVARCHAR(64) NOT NULL,
    [UniqueCodeType] NVARCHAR(8) NOT NULL,
    [UniqueCode] NVARCHAR(96) NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [FiscalStatus] NVARCHAR(48) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_FiscalDocuments] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_FiscalDocuments_Businesses]
      FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_FiscalDocuments_Business_Number]
      UNIQUE ([BusinessId],[FiscalDocumentType],[FiscalNumber]),
    CONSTRAINT [CK_FiscalDocuments_Type]
      CHECK ([FiscalDocumentType] IN (N'Invoice',N'CreditNote')),
    CONSTRAINT [CK_FiscalDocuments_UniqueCodeType]
      CHECK (([FiscalDocumentType]=N'Invoice' AND [UniqueCodeType]=N'CUFE') OR
             ([FiscalDocumentType]=N'CreditNote' AND [UniqueCodeType]=N'CUDE'))
);
GO
CREATE INDEX [IX_FiscalDocuments_Business_Status_Issued]
  ON [dbo].[FiscalDocuments] ([BusinessId],[FiscalStatus],[IssuedAt],[DocumentId]);
GO
