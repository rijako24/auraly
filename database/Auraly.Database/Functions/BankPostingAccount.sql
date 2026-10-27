-- Automatic transfer capture uses the most specific configured dimensions.
-- Equal-priority candidates remain ambiguous; never choose an arbitrary TOP(1).
CREATE FUNCTION accounting.BankPostingAccount(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@BankAccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    WITH candidates AS
    (
        SELECT account.AccountId,
          CASE WHEN account.BusinessId=@BusinessId THEN 2 ELSE 0 END
            +CASE WHEN account.PaymentMethodOptionId IS NOT NULL THEN 1 ELSE 0 END Priority
        FROM dbo.AccountingAccounts account WITH(HOLDLOCK)
        JOIN accounting.BankAccounts bank WITH(HOLDLOCK)
          ON bank.BankAccountId=account.BankAccountId AND bank.TenantId=account.TenantId
        LEFT JOIN reference.Options method ON method.OptionId=account.PaymentMethodOptionId
          AND method.CatalogCode=N'payment-method' AND method.IsActive=1
        WHERE account.TenantId=@TenantId AND bank.BankAccountId=@BankAccountId AND bank.IsActive=1
          AND account.FinancialFunction=N'Bank' AND account.AccountType=N'Asset'
          AND account.IsActive=1 AND account.AllowsPosting=1 AND account.RequiresParty=0
          AND (account.BusinessId IS NULL OR account.BusinessId=@BusinessId)
          AND (account.PaymentMethodOptionId IS NULL OR method.Code=N'Transfer')
    )
    SELECT CASE WHEN COUNT(*)=1 THEN CONVERT(uniqueidentifier,MAX(CONVERT(nvarchar(36),AccountId))) END AccountId,
      COUNT(*) CandidateCount
    FROM candidates WHERE Priority=(SELECT MAX(Priority) FROM candidates)
);
GO
