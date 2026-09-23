IF COL_LENGTH(N'dbo.Expenses', N'PurchaseEvidenceType') IS NULL
BEGIN
    ALTER TABLE dbo.Expenses ADD PurchaseEvidenceType nvarchar(40) NULL;

    UPDATE dbo.Expenses
    SET PurchaseEvidenceType = CASE
        WHEN SourceInvoiceId IS NOT NULL THEN N'InternalReceiptVoucher'
        ELSE N'SupplierElectronicInvoice'
    END
    WHERE PurchaseEvidenceType IS NULL;

    ALTER TABLE dbo.Expenses ALTER COLUMN PurchaseEvidenceType nvarchar(40) NOT NULL;
END;

UPDATE dbo.Expenses
SET PurchaseEvidenceType=N'InternalReceiptVoucher'
WHERE SourceInvoiceId IS NOT NULL
  AND PurchaseEvidenceType=N'SupplierElectronicInvoice';

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.Expenses') AND name=N'CK_Expenses_EvidenceOrigin')
    ALTER TABLE dbo.Expenses DROP CONSTRAINT CK_Expenses_EvidenceOrigin;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.Expenses') AND name=N'CK_Expenses_EvidenceOrigin')
    ALTER TABLE dbo.Expenses ADD CONSTRAINT CK_Expenses_EvidenceOrigin CHECK
      (SourceInvoiceId IS NOT NULL OR PurchaseEvidenceType<>N'SupplierElectronicInvoice' OR SupplierDocumentNumber IS NOT NULL);

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.Expenses') AND name=N'CK_Expenses_PurchaseEvidenceType')
    ALTER TABLE dbo.Expenses ADD CONSTRAINT CK_Expenses_PurchaseEvidenceType CHECK
      (PurchaseEvidenceType IN (N'SupplierElectronicInvoice',N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher'));
