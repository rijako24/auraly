-- Canonical account policy, without mutation or first-use locks. Configuration
-- diagnostics and the locking posting adapter consume this same predicate.
CREATE FUNCTION accounting.PostingAccountEligibility(
    @TenantId uniqueidentifier,@Category nvarchar(128),@AccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    SELECT account.AccountId FROM dbo.AccountingAccounts account
    JOIN dbo.AccountingConfigurationProfileAccounts definition ON definition.Category=@Category
      AND (definition.AccountType=account.AccountType OR
        definition.AccountType=N'Expense' AND account.AccountType=N'Cost')
    JOIN dbo.AccountingConfigurationProfiles profile ON profile.ProfileCode=definition.ProfileCode
      AND profile.IsDefault=1 AND profile.IsActive=1
    WHERE account.AccountId=@AccountId AND account.TenantId=@TenantId
      AND account.IsActive=1 AND account.AllowsPosting=1
      AND (account.FinancialFunction IS NULL
        OR EXISTS(SELECT 1 FROM accounting.PostingCategoryControlFunction(@Category) control
          WHERE account.FinancialFunction=control.FinancialFunction))
      AND (@Category NOT IN(N'SupplierDebitCard',N'SupplierCreditCard') OR (
        account.FinancialFunction IS NOT NULL
        AND EXISTS(SELECT 1 FROM accounting.BankAccounts bank WHERE bank.BankAccountId=account.BankAccountId
          AND bank.TenantId=@TenantId AND bank.IsActive=1)
        AND (account.PaymentMethodOptionId IS NULL OR EXISTS(SELECT 1 FROM reference.Options method
          WHERE method.OptionId=account.PaymentMethodOptionId AND method.CatalogCode=N'payment-method' AND method.IsActive=1
            AND method.Code=CASE WHEN @Category=N'SupplierDebitCard' THEN N'DebitCard' ELSE N'CreditCard' END))))
);
GO
