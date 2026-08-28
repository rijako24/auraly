SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LegacyNormalizationNow DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();

BEGIN TRANSACTION;

-- Antes de asignar un codigo interno, completa los campos canonicos que el catalogo
-- exige. Se conserva cualquier impuesto existente; solo se crea un perfil exento
-- para negocios heredados que nunca tuvieron maestro fiscal.
INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
SELECT NEWID(),businessValue.BusinessId,N'EXEMPT',N'01',N'Exento',0,1,@LegacyNormalizationNow
FROM dbo.Businesses businessValue
WHERE EXISTS(
    SELECT 1 FROM dbo.Products productValue
    WHERE productValue.BusinessId=businessValue.BusinessId
      AND productValue.TaxProfileId IS NULL)
  AND NOT EXISTS(
    SELECT 1 FROM dbo.TaxProfiles taxProfile
    WHERE taxProfile.BusinessId=businessValue.BusinessId);

UPDATE productValue
SET BaseUnitCode=COALESCE(NULLIF(LTRIM(RTRIM(productValue.BaseUnitCode)),N''),N'EA'),
    TaxProfileId=COALESCE(productValue.TaxProfileId,defaultTax.TaxProfileId),
    UpdatedAt=COALESCE(productValue.UpdatedAt,SYSUTCDATETIME())
FROM dbo.Products productValue
OUTER APPLY(
    SELECT TOP(1) taxProfile.TaxProfileId
    FROM dbo.TaxProfiles taxProfile
    WHERE taxProfile.BusinessId=productValue.BusinessId
    ORDER BY taxProfile.IsActive DESC,
             CASE WHEN taxProfile.Rate=0 THEN 1 ELSE 0 END,
             taxProfile.CreatedAt,
             taxProfile.TaxProfileId
) defaultTax
WHERE productValue.BaseUnitCode IS NULL
   OR LTRIM(RTRIM(productValue.BaseUnitCode))=N''
   OR productValue.TaxProfileId IS NULL;

-- Los códigos anteriores al catálogo canónico se normalizan una sola vez por negocio.
-- Si todos los productos ya usan PRD-######, las publicaciones posteriores no los renumeran.
SELECT p.BusinessId,p.ProductId,
       ROW_NUMBER() OVER(PARTITION BY p.BusinessId ORDER BY p.CreatedAt,p.ProductId) AS SequenceNumber
INTO #ProductCodeNormalization
FROM dbo.Products p
WHERE EXISTS(
    SELECT 1
    FROM dbo.Products candidate
    WHERE candidate.BusinessId=p.BusinessId
      AND (candidate.ProductCode IS NULL
        OR LEN(candidate.ProductCode)<>10
        OR candidate.ProductCode NOT LIKE N'PRD-[0-9][0-9][0-9][0-9][0-9][0-9]'));

UPDATE productValue
SET ProductCode=N'LEGACY-'+CONVERT(nvarchar(36),productValue.ProductId)
FROM dbo.Products productValue
JOIN #ProductCodeNormalization normalization ON normalization.ProductId=productValue.ProductId;

UPDATE productValue
SET ProductCode=N'PRD-'+RIGHT(N'000000'+CONVERT(nvarchar(12),normalization.SequenceNumber),6),
    UpdatedAt=COALESCE(productValue.UpdatedAt,SYSUTCDATETIME())
FROM dbo.Products productValue
JOIN #ProductCodeNormalization normalization ON normalization.ProductId=productValue.ProductId;

-- Recupera el precio público exclusivamente desde su propietario canónico. La tabla
-- Products ya no conserva una copia heredada del precio.
SELECT productValue.ProductId,productValue.BusinessId,
       CAST(COALESCE(
         NULLIF(activePrice.Amount,0),
         historicalPrice.Amount
       ) AS decimal(19,4)) AS PublicAmount,
       CAST(COALESCE(taxProfile.Rate,0) AS decimal(9,6)) AS SalesTaxRate
INTO #LegacyProductPricing
FROM dbo.Products productValue
LEFT JOIN dbo.TaxProfiles taxProfile
  ON taxProfile.TaxProfileId=productValue.TaxProfileId
 AND taxProfile.BusinessId=productValue.BusinessId
OUTER APPLY(
  SELECT TOP(1) priceValue.Amount
  FROM dbo.ProductPrices priceValue
  WHERE priceValue.BusinessId=productValue.BusinessId
    AND priceValue.ProductId=productValue.ProductId
    AND priceValue.IsActive=1
  ORDER BY priceValue.ValidFrom DESC,priceValue.CreatedAt DESC
) activePrice
OUTER APPLY(
  SELECT TOP(1) priceValue.Amount
  FROM dbo.ProductPrices priceValue
  WHERE priceValue.BusinessId=productValue.BusinessId
    AND priceValue.ProductId=productValue.ProductId
    AND priceValue.Amount>0
  ORDER BY priceValue.ValidFrom DESC,priceValue.CreatedAt DESC
) historicalPrice
WHERE productValue.IsActive=1;

UPDATE priceValue
SET Amount=basis.PublicAmount,
    PreparedAmount=basis.PublicAmount,
    PublishedAt=COALESCE(priceValue.PublishedAt,priceValue.ValidFrom,priceValue.CreatedAt,@LegacyNormalizationNow)
FROM dbo.ProductPrices priceValue
JOIN #LegacyProductPricing basis
  ON basis.BusinessId=priceValue.BusinessId AND basis.ProductId=priceValue.ProductId
WHERE priceValue.IsActive=1
  AND priceValue.Amount<=0
  AND basis.PublicAmount>0;

INSERT dbo.ProductPrices(
  ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
  CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
  InputMode,RoundingIncrement,RoundingMode,PublishedAt,ValidFrom,IsActive,CreatedAt)
SELECT NEWID(),basis.BusinessId,basis.ProductId,basis.PublicAmount,basis.PublicAmount,N'COP',
       N'LegacySalePriceDerived',
       ROUND((basis.PublicAmount/(1+(basis.SalesTaxRate/100)))*0.90,6),
       10,10,N'Margin',1,N'Nearest',
       @LegacyNormalizationNow,@LegacyNormalizationNow,1,@LegacyNormalizationNow
FROM #LegacyProductPricing basis
WHERE basis.PublicAmount>0
  AND NOT EXISTS(
    SELECT 1 FROM dbo.ProductPrices currentPrice
    WHERE currentPrice.BusinessId=basis.BusinessId
      AND currentPrice.ProductId=basis.ProductId
      AND currentPrice.IsActive=1);

-- Solo completa registros heredados incompletos. Una configuración válida creada o
-- modificada por el usuario nunca se reemplaza durante despliegues posteriores.
UPDATE priceValue
SET PreparedAmount=priceValue.Amount,
    CostBasisType=N'LegacySalePriceDerived',
    CostBasisAmount=ROUND((priceValue.Amount/(1+(basis.SalesTaxRate/100)))*0.90,6),
    TargetMarginPercent=10,
    EffectiveMarginPercent=10,
    InputMode=N'Margin',
    RoundingIncrement=COALESCE(NULLIF(priceValue.RoundingIncrement,0),1),
    RoundingMode=COALESCE(priceValue.RoundingMode,N'Nearest'),
    PublishedAt=COALESCE(priceValue.PublishedAt,priceValue.ValidFrom,priceValue.CreatedAt,@LegacyNormalizationNow)
FROM dbo.ProductPrices priceValue
JOIN #LegacyProductPricing basis
  ON basis.BusinessId=priceValue.BusinessId AND basis.ProductId=priceValue.ProductId
WHERE priceValue.IsActive=1
  AND priceValue.Amount>0
  AND (priceValue.PreparedAmount<=0
    OR priceValue.CostBasisAmount IS NULL OR priceValue.CostBasisAmount<=0
    OR priceValue.TargetMarginPercent IS NULL OR priceValue.TargetMarginPercent<=0);

COMMIT TRANSACTION;
GO
