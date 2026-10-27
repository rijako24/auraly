-- All ledger auxiliaries of one real bank, including inactive historical ones.
-- Card clearing and credit-card liabilities are not deposited bank money.
CREATE FUNCTION accounting.BankBookAccounts(@TenantId uniqueidentifier,@BankAccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    SELECT AccountId FROM dbo.AccountingAccounts
    WHERE TenantId=@TenantId AND BankAccountId=@BankAccountId AND FinancialFunction=N'Bank'
);
GO
