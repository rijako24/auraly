-- Preserve the current draft that older releases stored on the active published row.
-- Historical drafts cannot be reconstructed; the migrated row is explicitly identified.
INSERT dbo.ProductPricePreparations
  (ProductPricePreparationId,BusinessId,ProductId,SourceProposalId,
   SourceDocumentId,SourceLineNumber,SourceProductId,PreparationOrigin,
   PublicAmountSnapshot,PreparedAmount,CostBasisType,CostBasisAmount,
   TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
   RoundingMode,Status,PreparedByUserId,PreparedAt)
SELECT NEWID(),price.BusinessId,price.ProductId,proposal.PriceRevisionProposalId,
       proposal.SourceDocumentId,proposal.SourceLineNumber,NULL,N'Migration',
       price.Amount,price.PreparedAmount,price.CostBasisType,price.CostBasisAmount,
       price.TargetMarginPercent,price.EffectiveMarginPercent,
       COALESCE(price.InputMode,N'SalePrice'),COALESCE(price.RoundingIncrement,1),
       COALESCE(price.RoundingMode,N'Nearest'),N'Pending',
       COALESCE(proposal.ReviewedByUserId,price.PublishedByUserId),
       COALESCE(proposal.ReviewedAt,price.PublishedAt,price.CreatedAt)
FROM dbo.ProductPrices price
OUTER APPLY (
  SELECT TOP(1) value.PriceRevisionProposalId,value.SourceDocumentId,
         value.SourceLineNumber,value.ReviewedByUserId,value.ReviewedAt
  FROM dbo.PriceRevisionProposals value
  WHERE value.BusinessId=price.BusinessId AND value.ProductId=price.ProductId
    AND value.Status IN(N'PendingReview',N'Approved')
  ORDER BY value.CreatedAt DESC,value.PriceRevisionProposalId DESC
) proposal
WHERE price.IsActive=1
  AND ABS(price.PreparedAmount-price.Amount)>=0.0001
  AND NOT EXISTS (
    SELECT 1 FROM dbo.ProductPricePreparations existing
    WHERE existing.BusinessId=price.BusinessId AND existing.ProductId=price.ProductId
      AND existing.Status=N'Pending');

-- From this release onward ProductPrices contains only the published snapshot.
UPDATE price
SET PreparedAmount=Amount
FROM dbo.ProductPrices price
WHERE price.IsActive=1 AND ABS(price.PreparedAmount-price.Amount)>=0.0001;
GO
