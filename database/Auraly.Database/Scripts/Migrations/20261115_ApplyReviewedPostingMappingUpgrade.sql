-- Deployment-only data transition. Its caller supplies reviewed identities;
-- configuration is never inferred from an account code/name or provisioned by
-- the runtime as a compatibility fallback. No source/job/ledger is rewritten.
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF ISNULL(ISJSON(@PostingMappingUpgradePlanJson),0)<>1 OR LEFT(LTRIM(@PostingMappingUpgradePlanJson),1)<>N'['
  THROW 51930,N'El plan revisado de cuentas contables no es un arreglo JSON válido.',1;

DECLARE @PostingMappingUpgradePlan table(
  TenantId uniqueidentifier NOT NULL,MappingId uniqueidentifier NOT NULL PRIMARY KEY,
  SourceAccountId uniqueidentifier NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,
  TargetAccountId uniqueidentifier NOT NULL,TargetCode nvarchar(32) NOT NULL,TargetName nvarchar(200) NOT NULL,
  Category nvarchar(128) NOT NULL,SourceCode nvarchar(32) NOT NULL,SourceName nvarchar(200) NOT NULL,
  SourceType nvarchar(32) NOT NULL,SourceFunction nvarchar(32) NULL,SourceSide nvarchar(16) NULL,
  SourceBankAccountId uniqueidentifier NULL,TargetType nvarchar(32) NOT NULL,
  TargetFunction nvarchar(32) NOT NULL,TargetSide nvarchar(16) NOT NULL);
INSERT @PostingMappingUpgradePlan
SELECT TenantId,MappingId,SourceAccountId,EffectiveFrom,EffectiveTo,TargetAccountId,TargetCode,TargetName,
  Category,SourceCode,SourceName,SourceType,SourceFunction,SourceSide,SourceBankAccountId,TargetType,TargetFunction,TargetSide
FROM OPENJSON(@PostingMappingUpgradePlanJson) WITH(
  TenantId uniqueidentifier '$.tenantId',MappingId uniqueidentifier '$.mappingId',
  SourceAccountId uniqueidentifier '$.sourceAccountId',EffectiveFrom date '$.effectiveFrom',EffectiveTo date '$.effectiveTo',
  TargetAccountId uniqueidentifier '$.targetAccountId',TargetCode nvarchar(32) '$.targetCode',TargetName nvarchar(200) '$.targetName',
  Category nvarchar(128) '$.category',SourceCode nvarchar(32) '$.sourceCode',SourceName nvarchar(200) '$.sourceName',
  SourceType nvarchar(32) '$.sourceType',SourceFunction nvarchar(32) '$.sourceFunction',SourceSide nvarchar(16) '$.sourceSide',
  SourceBankAccountId uniqueidentifier '$.sourceBankAccountId',TargetType nvarchar(32) '$.targetType',
  TargetFunction nvarchar(32) '$.targetFunction',TargetSide nvarchar(16) '$.targetSide');
IF EXISTS(SELECT 1 FROM @PostingMappingUpgradePlan WHERE SourceAccountId=TargetAccountId
    OR TargetCode LIKE N'%[^0-9]%' OR LEN(TargetCode)<>6
    OR NULLIF(LTRIM(RTRIM(TargetName)),N'') IS NULL OR EffectiveTo<EffectiveFrom)
  THROW 51930,N'El plan contable debe identificar una cuenta destino distinta, su código, nombre y rango válido.',1;

DECLARE @PostingMappingUpgradeActions table(
  TenantId uniqueidentifier NOT NULL,MappingId uniqueidentifier NOT NULL PRIMARY KEY,
  SourceAccountId uniqueidentifier NOT NULL,TargetAccountId uniqueidentifier NOT NULL,
  TargetCode nvarchar(32) NOT NULL,TargetName nvarchar(200) NOT NULL);

-- Exact reviewed mapping identity and range, never a search for similarly named
-- customer-owned accounts. Replays and mappings subsequently changed by users
-- are left alone; the shared policy still verifies their current readiness.
IF EXISTS(SELECT 1 FROM @PostingMappingUpgradePlan reviewed
  JOIN dbo.AccountingAccountMappings mapping WITH(UPDLOCK,HOLDLOCK)
    ON mapping.MappingId=reviewed.MappingId AND mapping.TenantId=reviewed.TenantId
  JOIN dbo.AccountingAccounts original WITH(UPDLOCK,HOLDLOCK)
    ON original.AccountId=mapping.AccountId AND original.TenantId=mapping.TenantId
  WHERE mapping.AccountId=reviewed.SourceAccountId
    AND NOT EXISTS(SELECT 1 FROM accounting.PostingAccountEligibility(reviewed.TenantId,reviewed.Category,original.AccountId)) AND
    (mapping.Category<>reviewed.Category OR mapping.BusinessId IS NOT NULL
      OR mapping.EffectiveFrom<>reviewed.EffectiveFrom
      OR EXISTS(SELECT mapping.EffectiveTo EXCEPT SELECT reviewed.EffectiveTo)))
  THROW 51931,N'El mapping contable cambió respecto del plan revisado; no se sobrescribió.',1;

INSERT @PostingMappingUpgradeActions
SELECT reviewed.TenantId,reviewed.MappingId,reviewed.SourceAccountId,reviewed.TargetAccountId,reviewed.TargetCode,reviewed.TargetName
FROM @PostingMappingUpgradePlan reviewed
JOIN dbo.AccountingAccountMappings mapping WITH(UPDLOCK,HOLDLOCK)
  ON mapping.MappingId=reviewed.MappingId AND mapping.TenantId=reviewed.TenantId
  AND mapping.AccountId=reviewed.SourceAccountId AND mapping.Category=reviewed.Category
JOIN dbo.AccountingAccounts source WITH(UPDLOCK,HOLDLOCK)
  ON source.AccountId=reviewed.SourceAccountId AND source.TenantId=reviewed.TenantId
WHERE NOT EXISTS(SELECT 1 FROM accounting.PostingAccountEligibility(reviewed.TenantId,reviewed.Category,source.AccountId));

IF EXISTS(SELECT 1 FROM @PostingMappingUpgradeActions action
  JOIN dbo.AccountingAccounts source ON source.AccountId=action.SourceAccountId
  JOIN @PostingMappingUpgradePlan reviewed ON reviewed.MappingId=action.MappingId
  WHERE source.Code<>reviewed.SourceCode OR source.Name COLLATE Latin1_General_100_BIN2<>reviewed.SourceName
    OR source.AccountType<>reviewed.SourceType
    OR source.IsActive<>1 OR source.AllowsPosting<>1 OR source.RequiresParty<>0
    OR source.ParentAccountId IS NOT NULL OR source.LegacyBusinessId IS NOT NULL
    OR EXISTS(SELECT source.FinancialFunction,source.BalanceSide,source.BankAccountId
      EXCEPT SELECT reviewed.SourceFunction,reviewed.SourceSide,reviewed.SourceBankAccountId)
    OR source.PaymentMethodOptionId IS NOT NULL)
  THROW 51932,N'La cuenta origen difiere de la configuración histórica revisada; no se modificó.',1;

-- Compliance mappings belong to their explicit report definitions, not to the
-- posting category. Copying them would also change their signed interpretation
-- across Asset/Liability. Require a reviewed fiscal plan rather than guessing.
IF EXISTS(SELECT 1 FROM @PostingMappingUpgradeActions action
  JOIN compliance.ComplianceConceptMappings fiscal WITH(UPDLOCK,HOLDLOCK)
    ON fiscal.TenantId=action.TenantId AND fiscal.AccountId=action.SourceAccountId)
  THROW 51937,N'La cuenta contable tiene mappings de cumplimiento; se requiere revisar explícitamente su destino fiscal antes de migrar.',1;

IF EXISTS(SELECT 1 FROM @PostingMappingUpgradeActions action
  JOIN dbo.AccountingAccounts existing WITH(UPDLOCK,HOLDLOCK)
    ON existing.AccountId=action.TargetAccountId OR (existing.TenantId=action.TenantId AND existing.Code=action.TargetCode)
  JOIN @PostingMappingUpgradePlan reviewed ON reviewed.MappingId=action.MappingId
  WHERE existing.AccountId<>action.TargetAccountId OR existing.TenantId<>action.TenantId
    OR existing.Code<>action.TargetCode OR existing.Name COLLATE Latin1_General_100_BIN2<>action.TargetName
    OR existing.AccountType<>reviewed.TargetType OR existing.IsActive<>1 OR existing.AllowsPosting<>1 OR existing.RequiresParty<>0
    OR existing.ParentAccountId IS NOT NULL OR existing.FinancialFunction<>reviewed.TargetFunction OR existing.FinancialFunction IS NULL
    OR existing.BalanceSide<>reviewed.TargetSide OR existing.BalanceSide IS NULL OR existing.LegacyBusinessId IS NOT NULL
    OR existing.BankAccountId IS NOT NULL OR existing.PaymentMethodOptionId IS NOT NULL)
  THROW 51933,N'El código o identidad de cuenta destino ya tiene otra configuración; no se sobrescribió.',1;

DECLARE @PostingMappingUpgradeNow datetimeoffset(7)=SYSDATETIMEOFFSET();
INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,
  CreatedAt,FinancialFunction,BalanceSide)
SELECT DISTINCT action.TargetAccountId,action.TenantId,action.TargetCode,action.TargetName,reviewed.TargetType,1,0,1,
  @PostingMappingUpgradeNow,reviewed.TargetFunction,reviewed.TargetSide
FROM @PostingMappingUpgradeActions action
JOIN @PostingMappingUpgradePlan reviewed ON reviewed.MappingId=action.MappingId
WHERE NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK) WHERE AccountId=action.TargetAccountId);
DECLARE @PostingAccountsCreated int=@@ROWCOUNT;

IF EXISTS(SELECT 1 FROM @PostingMappingUpgradeActions action
  JOIN @PostingMappingUpgradePlan reviewed ON reviewed.MappingId=action.MappingId WHERE
  NOT EXISTS(SELECT 1 FROM dbo.Businesses business WHERE business.TenantId=action.TenantId AND business.IsActive=1)
  OR EXISTS(SELECT 1 FROM dbo.Businesses business WHERE business.TenantId=action.TenantId AND business.IsActive=1
    AND NOT EXISTS(SELECT 1 FROM accounting.PostingAccountEligibility(action.TenantId,reviewed.Category,action.TargetAccountId))))
  THROW 51934,N'La cuenta contable destino no satisface la política contable canónica.',1;

UPDATE mapping SET AccountId=action.TargetAccountId
FROM dbo.AccountingAccountMappings mapping
JOIN @PostingMappingUpgradeActions action ON action.MappingId=mapping.MappingId AND action.TenantId=mapping.TenantId
WHERE mapping.AccountId=action.SourceAccountId;
DECLARE @PostingMappingsChanged int=@@ROWCOUNT;
IF @PostingMappingsChanged<>(SELECT COUNT(*) FROM @PostingMappingUpgradeActions)
  THROW 51936,N'El mapping contable cambió durante la migración; no se aplicó parcialmente.',1;

-- Do not publish a successful upgrade with an active Ready business that will
-- fail posting. Unreviewed/custom incompatibilities stop here without guesses.
DECLARE @UnresolvedPostingMappingUpgrade nvarchar(max);
SELECT @UnresolvedPostingMappingUpgrade=STRING_AGG(CONVERT(nvarchar(max),CONCAT(
  CONVERT(nvarchar(36),unresolved.TenantId),N'/',CONVERT(nvarchar(36),unresolved.BusinessId),N': ',unresolved.Category)),N'; ')
FROM (SELECT TOP(12) business.TenantId,business.BusinessId,
    COALESCE(required.Category,N'Perfil obligatorio no disponible') Category
  FROM dbo.Businesses business
  JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId AND tenant.IsActive=1
  JOIN dbo.AccountingTenantSettings settings ON settings.TenantId=business.TenantId AND settings.Status=N'Ready'
  OUTER APPLY accounting.RequiredPostingAccountReadiness(business.TenantId,business.BusinessId,
    CASE WHEN settings.EffectiveFrom>CONVERT(date,SWITCHOFFSET(@PostingMappingUpgradeNow,'+00:00')) THEN settings.EffectiveFrom
      ELSE CONVERT(date,SWITCHOFFSET(@PostingMappingUpgradeNow,'+00:00')) END) required
  WHERE business.IsActive=1 AND required.ReadyAccountId IS NULL
  ORDER BY business.TenantId,business.BusinessId,required.Category) unresolved;
IF @UnresolvedPostingMappingUpgrade IS NOT NULL
BEGIN
  DECLARE @PostingMappingUpgradeError nvarchar(2048)=LEFT(CONCAT(
    N'Hay configuración contable incompatible sin resolver; se requiere un plan explícito. ',@UnresolvedPostingMappingUpgrade),2048);
  THROW 51935,@PostingMappingUpgradeError,1;
END;
COMMIT TRANSACTION;
PRINT CONCAT(N'Migración contable revisada: cuentas creadas=',@PostingAccountsCreated,N'; mappings corregidos=',@PostingMappingsChanged,N'.');
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
