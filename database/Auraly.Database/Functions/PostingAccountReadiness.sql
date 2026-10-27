-- One readiness policy for live configuration and accepted mapping identities.
-- Only accounts actually requested for posting take the first-use update lock.
CREATE FUNCTION accounting.PostingAccountReadiness(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@Category nvarchar(128),@AccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    SELECT account.AccountId FROM dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.AccountingConfigurationProfileAccounts definition ON definition.Category=@Category
      AND definition.AccountType=account.AccountType
    JOIN dbo.AccountingConfigurationProfiles profile ON profile.ProfileCode=definition.ProfileCode
      AND profile.IsDefault=1 AND profile.IsActive=1
    WHERE account.AccountId=@AccountId AND account.TenantId=@TenantId
      AND account.IsActive=1 AND account.AllowsPosting=1
      AND (account.BusinessId IS NULL OR account.BusinessId=@BusinessId)
      AND (@Category NOT IN(N'SupplierDebitCard',N'SupplierCreditCard') OR (
        account.FinancialFunction=CASE WHEN @Category=N'SupplierDebitCard' THEN N'Bank' ELSE N'CreditCardLiability' END
        AND EXISTS(SELECT 1 FROM accounting.BankAccounts bank WHERE bank.BankAccountId=account.BankAccountId
          AND bank.TenantId=@TenantId AND bank.IsActive=1)
        AND (account.PaymentMethodOptionId IS NULL OR EXISTS(SELECT 1 FROM reference.Options method
          WHERE method.OptionId=account.PaymentMethodOptionId AND method.CatalogCode=N'payment-method' AND method.IsActive=1
            AND method.Code=CASE WHEN @Category=N'SupplierDebitCard' THEN N'DebitCard' ELSE N'CreditCard' END))))
);
GO
