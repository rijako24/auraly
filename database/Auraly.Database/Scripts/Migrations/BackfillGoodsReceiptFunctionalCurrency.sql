SET NOCOUNT ON;

UPDATE dbo.GoodsReceipts
SET ExchangeRate=1,
    ExchangeRateDate=CONVERT(date,COALESCE(SupplierInvoiceDate,ReceivedAt)),
    ExchangeRateSource=N'LegacyFunctionalCurrency',
    FunctionalNetAmount=NetAmount,
    FunctionalTaxAmount=TaxAmount,
    FunctionalGrandTotal=GrandTotal
WHERE FunctionalGrandTotal=0 AND GrandTotal>0;

UPDATE dbo.GoodsReceiptLines
SET FunctionalNetAmount=NetAmount,
    FunctionalTaxAmount=TaxAmount,
    FunctionalLineTotal=LineTotal,
    RecognizedInventoryCostAmount=NetAmount+
      CASE WHEN TaxTreatment=N'CapitalizedCost' THEN TaxAmount ELSE 0 END
WHERE FunctionalLineTotal=0 AND LineTotal>0;

UPDATE dbo.Payables
SET FunctionalOriginalAmount=OriginalAmount,
    FunctionalOutstandingAmount=OutstandingAmount,
    ExchangeRate=1
WHERE FunctionalOriginalAmount IS NULL;
