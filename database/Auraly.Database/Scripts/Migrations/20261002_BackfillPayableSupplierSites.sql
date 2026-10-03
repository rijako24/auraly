-- Freeze the historical payable at the supplier's current primary site.
-- Abort rather than silently attribute a balance to an unknown site.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (
    SELECT 1 FROM dbo.Payables payable
    JOIN dbo.Suppliers supplier ON supplier.SupplierId=payable.SupplierId
    WHERE payable.PartySiteId IS NULL
      AND (SELECT COUNT(*) FROM dbo.PartySites site
           WHERE site.PartyId=supplier.PartyId AND site.IsActive=1 AND site.IsPrimary=1)<>1)
    THROW 51840,'Payables without exactly one active primary supplier site require manual review before migration.',1;

UPDATE payable SET PartySiteId=site.PartySiteId
FROM dbo.Payables payable
JOIN dbo.Suppliers supplier ON supplier.SupplierId=payable.SupplierId
JOIN dbo.PartySites site ON site.PartyId=supplier.PartyId AND site.IsActive=1 AND site.IsPrimary=1
WHERE payable.PartySiteId IS NULL;

IF EXISTS (
    SELECT 1 FROM dbo.SupplierCredits credit
    JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId
    WHERE credit.PartySiteId IS NULL
      AND (SELECT COUNT(*) FROM dbo.PartySites site
           WHERE site.PartyId=supplier.PartyId AND site.IsActive=1 AND site.IsPrimary=1)<>1)
    THROW 51841,'Supplier credits without exactly one active primary site require review before migration.',1;

UPDATE credit SET PartySiteId=site.PartySiteId
FROM dbo.SupplierCredits credit
JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId
JOIN dbo.PartySites site ON site.PartyId=supplier.PartyId AND site.IsActive=1 AND site.IsPrimary=1
WHERE credit.PartySiteId IS NULL;
COMMIT TRANSACTION;
