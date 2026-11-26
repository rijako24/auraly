-- Specializes the existing posting readiness policy for tenant payment forms.
-- This predicate is read-only; posting locks its selected account rows explicitly.
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
    CROSS APPLY accounting.PostingAccountEligibility(@TenantId,category.Category,account.AccountId) readiness
    OUTER APPLY(
      SELECT COUNT(DISTINCT form.BankAccountId) BankCount,
        CONVERT(uniqueidentifier,MAX(CONVERT(nvarchar(36),form.BankAccountId))) BankAccountId
      FROM reference.PaymentMethods form
      JOIN reference.Options type ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method' AND type.Code=N'Transfer'
      WHERE form.TenantId=@TenantId AND form.AccountId=@AccountId AND form.IsActive=1 AND form.BankAccountId IS NOT NULL
    ) transfer
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
          AND bank.BankAccountId=CASE WHEN @MethodCode=N'Transfer' THEN transfer.BankAccountId ELSE account.BankAccountId END AND bank.IsActive=1))
      AND (@MethodCode<>N'Transfer' OR (
        transfer.BankCount=1 AND (account.BankAccountId IS NULL OR account.BankAccountId=transfer.BankAccountId)
        AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntryLines line
          WHERE line.AccountId=@AccountId AND line.BankAccountId IS NOT NULL AND line.BankAccountId<>transfer.BankAccountId)))
      AND (account.PaymentMethodOptionId IS NULL OR EXISTS(
        SELECT 1 FROM reference.Options type WHERE type.OptionId=account.PaymentMethodOptionId
          AND type.CatalogCode=N'payment-method' AND type.Code=@MethodCode AND type.IsActive=1))
);
GO
