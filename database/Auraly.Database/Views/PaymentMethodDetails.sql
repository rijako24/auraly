CREATE VIEW reference.PaymentMethodDetails AS
SELECT form.TenantId,form.PaymentMethodId,form.Code,form.Name,form.PaymentTypeId,
    type.Code MethodCode,type.Label TypeLabel,form.AccountId,account.Code AccountCode,
    account.Name AccountName,account.BankAccountId,form.UseForSales,form.UseForReceipts,
    form.UseForSupplierPayments,form.UseForPayroll,form.UseForVouchers,form.IsActive,form.SortOrder,form.Version,
    form.RowVersion,
    CONVERT(bit,CASE WHEN type.IsActive=1
      AND (form.UseForSales=0 OR ready.Sales=1)
      AND (form.UseForReceipts=0 OR ready.Receipts=1)
      AND (form.UseForSupplierPayments=0 OR ready.Suppliers=1)
      AND (form.UseForPayroll=0 OR ready.Receipts=1)
      AND (form.UseForVouchers=0 OR ready.Receipts=1 OR ready.Suppliers=1)
      AND account.IsActive=1 AND account.AllowsPosting=1 THEN 1 ELSE 0 END) IsReady,
    CONVERT(bit,COALESCE(ready.Sales,0)) IsSalesAccountReady,
    CONVERT(bit,COALESCE(ready.Receipts,0)) IsReceiptAccountReady,
    CONVERT(bit,COALESCE(ready.Suppliers,0)) IsSupplierPaymentAccountReady
FROM reference.PaymentMethods form
JOIN reference.Options type ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method'
JOIN dbo.AccountingAccounts account ON account.TenantId=form.TenantId AND account.AccountId=form.AccountId
OUTER APPLY(
    SELECT MAX(CASE WHEN readiness.SourceType=N'PosPaymentMethod' THEN 1 ELSE 0 END) Sales,
      MAX(CASE WHEN readiness.SourceType=N'CustomerPaymentMethod' THEN 1 ELSE 0 END) Receipts,
      MAX(CASE WHEN readiness.SourceType=N'SupplierPaymentMethod' THEN 1 ELSE 0 END) Suppliers
    FROM accounting.PaymentMethodAccountReadiness(form.TenantId,type.Code,form.AccountId) readiness
) ready;
GO
