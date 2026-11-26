-- Additive catalog preparation. Contract versions, historical sources,
-- tenders and accounts are not rewritten by this script.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

SELECT DISTINCT account.TenantId,account.AccountId,method.OptionId PaymentTypeId,
    method.Code MethodCode,account.Code AccountCode,account.Name AccountName,
    mapping.SourceType
INTO #PaymentMethodCandidates
FROM dbo.Businesses business
JOIN dbo.AccountingConfigurationProfiles profile ON profile.IsDefault=1 AND profile.IsActive=1
JOIN dbo.AccountingSourceCategoryMappings mapping ON mapping.ProfileCode=profile.ProfileCode
    AND mapping.SourceType IN(N'PosPaymentMethod',N'CustomerPaymentMethod',N'SupplierPaymentMethod')
JOIN reference.Options method ON method.CatalogCode=N'payment-method' AND method.Code=mapping.SourceCode AND method.IsActive=1
CROSS APPLY accounting.ConfiguredPostingAccount(business.TenantId,business.BusinessId,mapping.Category,
    CONVERT(date,SYSUTCDATETIME())) selected
JOIN dbo.AccountingAccounts account ON account.AccountId=selected.AccountId AND account.TenantId=business.TenantId
CROSS APPLY accounting.PaymentMethodAccountReadiness(account.TenantId,method.Code,account.AccountId) readiness
WHERE readiness.SourceType=mapping.SourceType;

-- Real bank auxiliaries supplement generic clearing mappings. The account is
-- already explicit; no bank name, number or auxiliary is invented.
INSERT #PaymentMethodCandidates
SELECT DISTINCT account.TenantId,account.AccountId,method.OptionId,method.Code,account.Code,account.Name,usage.SourceType
FROM dbo.AccountingAccounts account
JOIN accounting.BankAccounts bank ON bank.TenantId=account.TenantId AND bank.BankAccountId=account.BankAccountId AND bank.IsActive=1
JOIN reference.Options method ON method.CatalogCode=N'payment-method' AND method.Code=N'Transfer' AND method.IsActive=1
CROSS JOIN (VALUES(N'PosPaymentMethod'),(N'CustomerPaymentMethod'),(N'SupplierPaymentMethod')) usage(SourceType)
CROSS APPLY accounting.PaymentPostingCategory(usage.SourceType,method.Code) category
CROSS APPLY accounting.PostingAccountEligibility(account.TenantId,category.Category,account.AccountId) readiness
WHERE account.FinancialFunction=N'Bank'
    AND (account.PaymentMethodOptionId IS NULL OR account.PaymentMethodOptionId=method.OptionId)
    AND NOT EXISTS(SELECT 1 FROM #PaymentMethodCandidates candidate WHERE candidate.AccountId=account.AccountId
        AND candidate.PaymentTypeId=method.OptionId AND candidate.SourceType=usage.SourceType);

-- Bootstrap the explicit legacy bank binding inside this transaction before
-- applying form readiness, which now reads the form's own BankAccountId.
DECLARE @CreatedPaymentMethods TABLE(PaymentMethodId uniqueidentifier PRIMARY KEY);
INSERT reference.PaymentMethods(TenantId,PaymentMethodId,Code,Name,PaymentTypeId,AccountId,BankAccountId,
    UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,IsActive,SortOrder,Version,CreatedAt,UpdatedAt)
OUTPUT inserted.PaymentMethodId INTO @CreatedPaymentMethods
SELECT candidate.TenantId,NEWID(),CONCAT(candidate.MethodCode,N'-',candidate.AccountCode),LEFT(candidate.AccountName,160),
    candidate.PaymentTypeId,candidate.AccountId,
    CASE WHEN candidate.MethodCode=N'Transfer' THEN account.BankAccountId END,
    MAX(CASE WHEN candidate.SourceType=N'PosPaymentMethod' THEN 1 ELSE 0 END),
    MAX(CASE WHEN candidate.SourceType=N'CustomerPaymentMethod' THEN 1 ELSE 0 END),
    MAX(CASE WHEN candidate.SourceType=N'SupplierPaymentMethod' THEN 1 ELSE 0 END),
    MAX(CASE WHEN candidate.SourceType=N'CustomerPaymentMethod' AND candidate.MethodCode IN(N'Cash',N'Transfer') THEN 1 ELSE 0 END),
    1,10,1,SYSUTCDATETIME(),SYSUTCDATETIME()
FROM #PaymentMethodCandidates candidate
JOIN dbo.AccountingAccounts account ON account.AccountId=candidate.AccountId AND account.TenantId=candidate.TenantId
WHERE NOT EXISTS(SELECT 1 FROM reference.PaymentMethods existing WITH(UPDLOCK,HOLDLOCK)
    WHERE existing.TenantId=candidate.TenantId AND (existing.Code=CONCAT(candidate.MethodCode,N'-',candidate.AccountCode)
      OR existing.PaymentTypeId=candidate.PaymentTypeId AND existing.AccountId=candidate.AccountId))
GROUP BY candidate.TenantId,candidate.AccountId,candidate.PaymentTypeId,candidate.MethodCode,candidate.AccountCode,candidate.AccountName,account.BankAccountId;

IF EXISTS(SELECT 1 FROM @CreatedPaymentMethods created
    JOIN reference.PaymentMethods form ON form.PaymentMethodId=created.PaymentMethodId
    JOIN reference.Options type ON type.OptionId=form.PaymentTypeId
    CROSS APPLY(VALUES(N'PosPaymentMethod',form.UseForSales),(N'CustomerPaymentMethod',form.UseForReceipts),
      (N'SupplierPaymentMethod',form.UseForSupplierPayments)) usage(SourceType,Enabled)
    WHERE usage.Enabled=1 AND NOT EXISTS(SELECT 1 FROM accounting.PaymentMethodAccountReadiness(form.TenantId,type.Code,form.AccountId) ready
      WHERE ready.SourceType=usage.SourceType))
    THROW 51987,N'Una forma migrada no conserva un banco y auxiliar compatibles; se revirtió la preparación del catálogo.',1;

DROP TABLE #PaymentMethodCandidates;
COMMIT;
GO
