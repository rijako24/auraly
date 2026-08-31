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
    [DeliveryEmail] NVARCHAR(254) NULL,
    [DeliveryOutboxMessageId] UNIQUEIDENTIFIER NULL,
    [DeliveredAt] DATETIMEOFFSET(7) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_FiscalDocuments] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_FiscalDocuments_Businesses]
      FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_FiscalDocuments_DeliveryOutbox]
      FOREIGN KEY ([DeliveryOutboxMessageId]) REFERENCES [dbo].[TenantProvisioningOutboxMessages] ([MessageId]),
    CONSTRAINT [UQ_FiscalDocuments_Business_Number]
      UNIQUE ([BusinessId],[FiscalDocumentType],[FiscalNumber]),
    CONSTRAINT [CK_FiscalDocuments_Type]
      CHECK ([FiscalDocumentType] IN (N'Invoice',N'CreditNote',N'DebitNote',N'SupportDocument',N'ElectronicPayroll')),
    CONSTRAINT [CK_FiscalDocuments_UniqueCodeType]
      CHECK (([FiscalDocumentType]=N'Invoice' AND [UniqueCodeType]=N'CUFE') OR
             ([FiscalDocumentType] IN (N'CreditNote',N'DebitNote') AND [UniqueCodeType]=N'CUDE') OR
             ([FiscalDocumentType]=N'SupportDocument' AND [UniqueCodeType]=N'CUDS') OR
             ([FiscalDocumentType]=N'ElectronicPayroll' AND [UniqueCodeType]=N'CUNE'))
);
GO
CREATE UNIQUE INDEX [UX_FiscalDocuments_DeliveryOutbox]
  ON [dbo].[FiscalDocuments] ([DeliveryOutboxMessageId])
  WHERE [DeliveryOutboxMessageId] IS NOT NULL;
GO
CREATE INDEX [IX_FiscalDocuments_Business_Status_Issued]
  ON [dbo].[FiscalDocuments] ([BusinessId],[FiscalStatus],[IssuedAt],[DocumentId]);
GO
