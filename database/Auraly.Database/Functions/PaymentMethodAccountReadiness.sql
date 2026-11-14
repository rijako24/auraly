-- Specializes the existing posting readiness policy for tenant payment forms.
-- A bank/clearing/credit auxiliary keeps its financial meaning in every consumer.
CREATE FUNCTION accounting.PaymentMethodAccountReadiness(
    @TenantId uniqueidentifier,@MethodCode nvarchar(32),@AccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    SELECT usage.SourceType,account.AccountId
    FROM dbo.AccountingAccounts account
    CROSS JOIN (VALUES(N'PosPaymentMethod'),(N'CustomerPaymentMethod'),(N'SupplierPaymentMethod')) usage(SourceType)
    CROSS APPLY accounting.PaymentPostingCategory(usage.SourceType,@MethodCode) category
    CROSS APPLY accounting.PostingAccountReadiness(@TenantId,category.Category,account.AccountId) readiness
    WHERE account.TenantId=@TenantId AND account.AccountId=@AccountId
      AND account.FinancialFunction=CASE
        WHEN @MethodCode=N'Cash' THEN N'Cash'
        WHEN @MethodCode=N'Transfer' THEN N'Bank'
        WHEN @MethodCode=N'Credit' AND usage.SourceType=N'PosPaymentMethod' THEN N'Receivable'
        WHEN @MethodCode=N'DebitCard' AND usage.SourceType=N'SupplierPaymentMethod' THEN N'Bank'
        WHEN @MethodCode=N'CreditCard' AND usage.SourceType=N'SupplierPaymentMethod' THEN N'CreditCardLiability'
        WHEN @MethodCode IN(N'DebitCard',N'CreditCard') THEN N'CardClearing' END
      AND (account.FinancialFunction NOT IN(N'Bank',N'CreditCardLiability') OR EXISTS(
        SELECT 1 FROM accounting.BankAccounts bank WHERE bank.TenantId=@TenantId
          AND bank.BankAccountId=account.BankAccountId AND bank.IsActive=1))
      AND (account.PaymentMethodOptionId IS NULL OR EXISTS(
        SELECT 1 FROM reference.Options type WHERE type.OptionId=account.PaymentMethodOptionId
          AND type.CatalogCode=N'payment-method' AND type.Code=@MethodCode AND type.IsActive=1))
);
GO
