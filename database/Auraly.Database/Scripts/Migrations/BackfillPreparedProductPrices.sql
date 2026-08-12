-- Idempotent bridge for prices created before PreparedAmount existed.
-- A deliberately prepared zero has InputMode set, so it is never overwritten here.
UPDATE dbo.ProductPrices
SET PreparedAmount=Amount
WHERE PreparedAmount=0
  AND Amount>0
  AND InputMode IS NULL
  AND TargetMarginPercent IS NULL
  AND EffectiveMarginPercent IS NULL;
GO