-- One-time, idempotent migration: old product master values become a canonical
-- ProductPrices record only when the product does not already have an active price.
-- ProductPrices is authoritative after this migration; Products.UnitPrice is not mirrored.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

INSERT dbo.ProductPrices
  (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
SELECT NEWID(),p.BusinessId,p.ProductId,p.UnitPrice,p.UnitPrice,
       LEFT(UPPER(COALESCE(NULLIF(LTRIM(RTRIM(p.Currency)),N''),N'COP')),3),
       SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
FROM dbo.Products p WITH (UPDLOCK,HOLDLOCK)
WHERE p.IsActive=1
  AND p.UnitPrice > 0
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.ProductPrices pp WITH (UPDLOCK,HOLDLOCK)
    WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId AND pp.IsActive=1);

COMMIT TRANSACTION;
GO