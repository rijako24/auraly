-- Automatic transfer capture uses the most specific configured dimensions.
-- Equal-priority candidates remain ambiguous; never choose an arbitrary TOP(1).
CREATE FUNCTION accounting.BankPostingAccount(
    @TenantId uniqueidentifier,@BankAccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    WITH candidates AS
    (
        SELECT account.AccountId,
          CASE WHEN account.PaymentMethodOptionId IS NOT NULL THEN 1 ELSE 0 END Priority
        FROM dbo.AccountingAccounts account WITH(HOLDLOCK)
        JOIN accounting.BankAccounts bank WITH(HOLDLOCK)
          ON bank.BankAccountId=account.BankAccountId AND bank.TenantId=account.TenantId
        LEFT JOIN reference.Options method ON method.OptionId=account.PaymentMethodOptionId
          AND method.CatalogCode=N'payment-method' AND method.IsActive=1
        WHERE account.TenantId=@TenantId AND bank.BankAccountId=@BankAccountId AND bank.IsActive=1
          AND account.FinancialFunction=N'Bank' AND account.AccountType=N'Asset'
          AND account.IsActive=1 AND account.AllowsPosting=1 AND account.RequiresParty=0
          AND (account.PaymentMethodOptionId IS NULL OR method.Code=N'Transfer')
        UNION
        SELECT form.AccountId,2
        FROM reference.PaymentMethods form
        JOIN reference.Options type ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method'
          AND type.Code=N'Transfer' AND type.IsActive=1
        WHERE form.TenantId=@TenantId AND form.BankAccountId=@BankAccountId AND form.IsActive=1
          AND EXISTS(SELECT 1 FROM accounting.PaymentMethodAccountReadiness(@TenantId,N'Transfer',form.AccountId))
    )
    SELECT CASE WHEN COUNT(*)=1 THEN CONVERT(uniqueidentifier,MAX(CONVERT(nvarchar(36),AccountId))) END AccountId,
      COUNT(*) CandidateCount
    FROM candidates WHERE Priority=(SELECT MAX(Priority) FROM candidates)
);
GO
