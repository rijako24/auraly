-- Native transfer forms own their bank selection. Copy only the explicit bank
-- already used by the former auxiliary relationship; never choose a primary bank.
UPDATE form SET BankAccountId=account.BankAccountId
FROM reference.PaymentMethods form
JOIN reference.Options type ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method' AND type.Code=N'Transfer'
JOIN dbo.AccountingAccounts account ON account.AccountId=form.AccountId AND account.TenantId=form.TenantId
JOIN accounting.BankAccounts bank ON bank.BankAccountId=account.BankAccountId AND bank.TenantId=form.TenantId
WHERE form.BankAccountId IS NULL;
GO
