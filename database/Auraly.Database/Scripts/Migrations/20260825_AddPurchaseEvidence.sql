IF OBJECT_ID(N'dbo.Suppliers',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Suppliers',N'PurchaseEvidencePolicy') IS NULL
    ALTER TABLE dbo.Suppliers ADD PurchaseEvidencePolicy NVARCHAR(40) NULL;
GO

IF OBJECT_ID(N'dbo.GoodsReceiptDrafts',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceiptDrafts',N'PurchaseEvidenceType') IS NULL
    ALTER TABLE dbo.GoodsReceiptDrafts ADD PurchaseEvidenceType NVARCHAR(40) NULL;
GO

IF OBJECT_ID(N'dbo.GoodsReceipts',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceipts',N'PurchaseEvidenceType') IS NULL
BEGIN
    ALTER TABLE dbo.GoodsReceipts ADD PurchaseEvidenceType NVARCHAR(40) NULL;
    UPDATE dbo.GoodsReceipts
    SET PurchaseEvidenceType=CASE
      WHEN SupplierInvoiceNumber IS NOT NULL OR SupplierInvoiceDate IS NOT NULL
        THEN N'SupplierElectronicInvoice'
      ELSE N'InternalReceiptVoucher'
    END
    WHERE PurchaseEvidenceType IS NULL;
    ALTER TABLE dbo.GoodsReceipts ALTER COLUMN PurchaseEvidenceType NVARCHAR(40) NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.GoodsReceipts',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceipts',N'SupportFiscalSeriesId') IS NULL
    ALTER TABLE dbo.GoodsReceipts ADD SupportFiscalSeriesId UNIQUEIDENTIFIER NULL;
IF OBJECT_ID(N'dbo.GoodsReceipts',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceipts',N'SupportFiscalAuthorizationId') IS NULL
    ALTER TABLE dbo.GoodsReceipts ADD SupportFiscalAuthorizationId UNIQUEIDENTIFIER NULL;
IF OBJECT_ID(N'dbo.GoodsReceipts',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GoodsReceipts',N'SupportFiscalNumber') IS NULL
    ALTER TABLE dbo.GoodsReceipts ADD SupportFiscalNumber NVARCHAR(64) NULL;
GO

IF OBJECT_ID(N'dbo.CK_FiscalDocuments_Type',N'C') IS NOT NULL
    ALTER TABLE dbo.FiscalDocuments DROP CONSTRAINT CK_FiscalDocuments_Type;
IF OBJECT_ID(N'dbo.CK_FiscalDocuments_UniqueCodeType',N'C') IS NOT NULL
    ALTER TABLE dbo.FiscalDocuments DROP CONSTRAINT CK_FiscalDocuments_UniqueCodeType;
GO
