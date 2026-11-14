-- Reviewed before DeployReport through the existing release pipeline. Stop old
-- accounting/configuration writers for the coordinated schema/application cut.
-- A scope-only rename preserves the old value as historical evidence; no account,
-- source, journal, balance, payment or mapping is rewritten here.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF COL_LENGTH(N'dbo.AccountingAccounts',N'BusinessId') IS NOT NULL
  AND COL_LENGTH(N'dbo.AccountingAccounts',N'BankAccountId') IS NOT NULL
  AND COL_LENGTH(N'dbo.AccountingAccounts',N'FinancialFunction') IS NOT NULL
  AND COL_LENGTH(N'dbo.AccountingAccounts',N'PaymentMethodOptionId') IS NOT NULL
  AND OBJECT_ID(N'accounting.BankAccounts',N'U') IS NOT NULL
BEGIN
  -- Retiring scope can change the implicit bank choice even when a different
  -- candidate would win uniquely. Require review rather than selecting one,
  -- generating another auxiliary or changing a customer's bank configuration.
  EXEC sys.sp_executesql N'
    DECLARE @Tenant uniqueidentifier,@Bank uniqueidentifier;
    SELECT TOP(1) @Tenant=account.TenantId,@Bank=account.BankAccountId
    FROM dbo.AccountingAccounts account
    JOIN accounting.BankAccounts bank ON bank.BankAccountId=account.BankAccountId
      AND bank.TenantId=account.TenantId AND bank.IsActive=1
    LEFT JOIN reference.Options method ON method.OptionId=account.PaymentMethodOptionId
      AND method.CatalogCode=N''payment-method'' AND method.IsActive=1
    WHERE account.IsActive=1 AND account.AllowsPosting=1 AND account.RequiresParty=0
      AND account.AccountType=N''Asset'' AND account.FinancialFunction=N''Bank''
      AND (account.PaymentMethodOptionId IS NULL OR method.Code=N''Transfer'')
    GROUP BY account.TenantId,account.BankAccountId
    HAVING COUNT(*)>1 AND MAX(CASE WHEN account.BusinessId IS NOT NULL THEN 1 ELSE 0 END)=1
    ORDER BY account.TenantId,account.BankAccountId;
    IF @Bank IS NOT NULL
    BEGIN
      DECLARE @Issue nvarchar(2048)=CONCAT(N''Revisa el vínculo bancario antes de compartir el PUC. Empresa: '',
        CONVERT(nvarchar(36),@Tenant),N''. Banco: '',CONVERT(nvarchar(36),@Bank),
        N''. Hay varios auxiliares y uno dependía de sede; el corte no elige ni reasigna cuentas automáticamente.'');
      THROW 51768,@Issue,1;
    END;';
END;
GO
