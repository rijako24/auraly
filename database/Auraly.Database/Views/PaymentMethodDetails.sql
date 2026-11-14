CREATE VIEW reference.PaymentMethodDetails AS
SELECT form.TenantId,form.PaymentMethodId,form.Code,form.Name,form.PaymentTypeId,
    type.Code MethodCode,type.Label TypeLabel,form.AccountId,account.Code AccountCode,
    account.Name AccountName,account.BankAccountId,form.UseForSales,form.UseForReceipts,
    form.UseForSupplierPayments,form.UseForPayroll,form.UseForVouchers,form.IsActive,form.SortOrder,form.Version,
    form.RowVersion,
    CONVERT(bit,CASE WHEN type.IsActive=1 AND NOT EXISTS(
      SELECT 1 FROM (VALUES(N'PosPaymentMethod',form.UseForSales),(N'CustomerPaymentMethod',form.UseForReceipts),
        (N'SupplierPaymentMethod',form.UseForSupplierPayments),(N'CustomerPaymentMethod',form.UseForPayroll)) usage(SourceType,Enabled)
      WHERE usage.Enabled=1 AND NOT EXISTS(
        SELECT 1 FROM accounting.PaymentMethodAccountReadiness(form.TenantId,type.Code,form.AccountId) readiness
        WHERE readiness.SourceType=usage.SourceType))
      AND (form.UseForVouchers=0 OR EXISTS(
        SELECT 1 FROM accounting.PaymentMethodAccountReadiness(form.TenantId,type.Code,form.AccountId) readiness
        WHERE readiness.SourceType IN(N'CustomerPaymentMethod',N'SupplierPaymentMethod')))
      AND account.IsActive=1 AND account.AllowsPosting=1 THEN 1 ELSE 0 END) IsReady
FROM reference.PaymentMethods form
JOIN reference.Options type ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method'
JOIN dbo.AccountingAccounts account ON account.TenantId=form.TenantId AND account.AccountId=form.AccountId;
GO
