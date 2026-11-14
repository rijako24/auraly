-- A concept classifies a counterpart. Treasury remains a payment form and
-- linked portfolio reductions keep their existing financial capture.
CREATE FUNCTION accounting.FinancialConceptAccountReadiness(
    @TenantId uniqueidentifier,@AccountId uniqueidentifier,@SideOptionId uniqueidentifier,@RequireReady bit)
RETURNS TABLE AS RETURN
(
    SELECT account.AccountId,account.FinancialFunction,side.Code SideCode,side.Label SideLabel
    FROM dbo.AccountingAccounts account JOIN reference.Options side ON side.OptionId=@SideOptionId
      AND side.CatalogCode=N'accounting-balance-side' AND side.Code IN(N'Debit',N'Credit')
    WHERE account.TenantId=@TenantId AND account.AccountId=@AccountId
      AND (@RequireReady=0 OR account.IsActive=1 AND account.AllowsPosting=1 AND side.IsActive=1
        AND (account.FinancialFunction=N'General' OR account.FinancialFunction=N'CustomerAdvance' AND side.Code=N'Credit'
          OR account.FinancialFunction=N'SupplierAdvance' AND side.Code=N'Debit'))
);
GO
