IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_FiscalDocuments_Type')
    ALTER TABLE dbo.FiscalDocuments DROP CONSTRAINT CK_FiscalDocuments_Type;
ALTER TABLE dbo.FiscalDocuments ALTER COLUMN FiscalDocumentType NVARCHAR(32) NOT NULL;
ALTER TABLE dbo.FiscalDocuments ADD CONSTRAINT CK_FiscalDocuments_Type
  CHECK (FiscalDocumentType IN (N'Invoice',N'CreditNote',N'DebitNote',N'SupportDocument',N'SupportDocumentAdjustment',N'ElectronicPayroll'));

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_FiscalDocuments_UniqueCodeType')
    ALTER TABLE dbo.FiscalDocuments DROP CONSTRAINT CK_FiscalDocuments_UniqueCodeType;
ALTER TABLE dbo.FiscalDocuments ADD CONSTRAINT CK_FiscalDocuments_UniqueCodeType
  CHECK ((FiscalDocumentType=N'Invoice' AND UniqueCodeType=N'CUFE') OR
         (FiscalDocumentType IN (N'CreditNote',N'DebitNote') AND UniqueCodeType=N'CUDE') OR
         (FiscalDocumentType IN (N'SupportDocument',N'SupportDocumentAdjustment') AND UniqueCodeType=N'CUDS') OR
         (FiscalDocumentType=N'ElectronicPayroll' AND UniqueCodeType=N'CUNE'));
GO
