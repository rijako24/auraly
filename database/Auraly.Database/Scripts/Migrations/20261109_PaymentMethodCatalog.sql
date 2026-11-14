-- Additive preparation only. ContractVersion remains 1 until the coordinated
-- server/web/Edge cutover. No previous source, tender or account is rewritten.
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
CROSS APPLY accounting.PaymentMethodAccountReadiness(account.TenantId,method.Code,account.AccountId) readiness
WHERE readiness.SourceType=usage.SourceType
    AND NOT EXISTS(SELECT 1 FROM #PaymentMethodCandidates candidate WHERE candidate.AccountId=account.AccountId
        AND candidate.PaymentTypeId=method.OptionId AND candidate.SourceType=usage.SourceType);

INSERT reference.PaymentMethods(TenantId,PaymentMethodId,Code,Name,PaymentTypeId,AccountId,
    UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,IsActive,SortOrder,Version,CreatedAt,UpdatedAt)
SELECT candidate.TenantId,NEWID(),CONCAT(candidate.MethodCode,N'-',candidate.AccountCode),LEFT(candidate.AccountName,160),
    candidate.PaymentTypeId,candidate.AccountId,
    MAX(CASE WHEN candidate.SourceType=N'PosPaymentMethod' THEN 1 ELSE 0 END),
    MAX(CASE WHEN candidate.SourceType=N'CustomerPaymentMethod' THEN 1 ELSE 0 END),
    MAX(CASE WHEN candidate.SourceType=N'SupplierPaymentMethod' THEN 1 ELSE 0 END),
    MAX(CASE WHEN candidate.SourceType=N'CustomerPaymentMethod' AND candidate.MethodCode IN(N'Cash',N'Transfer') THEN 1 ELSE 0 END),
    1,10,1,SYSUTCDATETIME(),SYSUTCDATETIME()
FROM #PaymentMethodCandidates candidate
WHERE NOT EXISTS(SELECT 1 FROM reference.PaymentMethods existing WITH(UPDLOCK,HOLDLOCK)
    WHERE existing.TenantId=candidate.TenantId AND (existing.Code=CONCAT(candidate.MethodCode,N'-',candidate.AccountCode)
      OR existing.PaymentTypeId=candidate.PaymentTypeId AND existing.AccountId=candidate.AccountId))
GROUP BY candidate.TenantId,candidate.AccountId,candidate.PaymentTypeId,candidate.MethodCode,candidate.AccountCode,candidate.AccountName;

DROP TABLE #PaymentMethodCandidates;
COMMIT;
GO
