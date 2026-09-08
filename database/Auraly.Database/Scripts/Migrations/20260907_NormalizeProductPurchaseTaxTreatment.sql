UPDATE product
SET PurchaseTaxTreatment = CASE
    WHEN purchaseTax.Rate = 0 THEN N'NotApplicable'
    WHEN product.PurchaseTaxTreatment = N'NotApplicable' THEN N'DeductibleInputVat'
    ELSE COALESCE(product.PurchaseTaxTreatment,N'DeductibleInputVat')
END
FROM dbo.Products product
JOIN dbo.TaxProfiles purchaseTax
  ON purchaseTax.TaxProfileId=COALESCE(product.PurchaseTaxProfileId,product.TaxProfileId)
 AND purchaseTax.BusinessId=product.BusinessId
WHERE (purchaseTax.Rate=0 AND COALESCE(product.PurchaseTaxTreatment,N'')<>N'NotApplicable')
   OR (purchaseTax.Rate>0 AND (product.PurchaseTaxTreatment=N'NotApplicable'
       OR product.PurchaseTaxTreatment IS NULL));
GO
