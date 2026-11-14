-- Deployment-only data transition. Its caller supplies reviewed identities;
-- configuration is never inferred from an account code/name or provisioned by
-- the runtime as a compatibility fallback. No source/job/ledger is rewritten.
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF ISNULL(ISJSON(@InputVatUpgradePlanJson),0)<>1 OR LEFT(LTRIM(@InputVatUpgradePlanJson),1)<>N'['
  THROW 51930,N'El plan revisado de IVA descontable no es un arreglo JSON válido.',1;

DECLARE @InputVatUpgradePlan table(
  TenantId uniqueidentifier NOT NULL,MappingId uniqueidentifier NOT NULL PRIMARY KEY,
  SourceAccountId uniqueidentifier NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,
  TargetAccountId uniqueidentifier NOT NULL,TargetCode nvarchar(32) NOT NULL,TargetName nvarchar(200) NOT NULL);
INSERT @InputVatUpgradePlan
SELECT TenantId,MappingId,SourceAccountId,EffectiveFrom,EffectiveTo,TargetAccountId,TargetCode,TargetName
FROM OPENJSON(@InputVatUpgradePlanJson) WITH(
  TenantId uniqueidentifier '$.tenantId',MappingId uniqueidentifier '$.mappingId',
  SourceAccountId uniqueidentifier '$.sourceAccountId',EffectiveFrom date '$.effectiveFrom',EffectiveTo date '$.effectiveTo',
  TargetAccountId uniqueidentifier '$.targetAccountId',TargetCode nvarchar(32) '$.targetCode',TargetName nvarchar(200) '$.targetName');
IF EXISTS(SELECT 1 FROM @InputVatUpgradePlan WHERE SourceAccountId=TargetAccountId
    OR TargetCode LIKE N'%[^0-9]%' OR LEN(TargetCode)<>6 OR LEFT(TargetCode,4)<>N'2408'
    OR NULLIF(LTRIM(RTRIM(TargetName)),N'') IS NULL OR EffectiveTo<EffectiveFrom)
  THROW 51930,N'El plan de IVA debe identificar una cuenta destino distinta, su código, nombre y rango válido.',1;

DECLARE @InputVatUpgradeActions table(
  TenantId uniqueidentifier NOT NULL,MappingId uniqueidentifier NOT NULL PRIMARY KEY,
  SourceAccountId uniqueidentifier NOT NULL,TargetAccountId uniqueidentifier NOT NULL,
  TargetCode nvarchar(32) NOT NULL,TargetName nvarchar(200) NOT NULL);

-- Exact reviewed mapping identity and range, never a search for similarly named
-- customer-owned accounts. Replays and mappings subsequently changed by users
-- are left alone; the shared policy still verifies their current readiness.
IF EXISTS(SELECT 1 FROM @InputVatUpgradePlan reviewed
  JOIN dbo.AccountingAccountMappings mapping WITH(UPDLOCK,HOLDLOCK)
    ON mapping.MappingId=reviewed.MappingId AND mapping.TenantId=reviewed.TenantId
  JOIN dbo.AccountingAccounts original WITH(UPDLOCK,HOLDLOCK)
    ON original.AccountId=mapping.AccountId AND original.TenantId=mapping.TenantId AND original.AccountType=N'Asset'
  WHERE mapping.AccountId=reviewed.SourceAccountId AND
    (mapping.Category<>N'InputVat' OR mapping.BusinessId IS NOT NULL
      OR mapping.EffectiveFrom<>reviewed.EffectiveFrom
      OR EXISTS(SELECT mapping.EffectiveTo EXCEPT SELECT reviewed.EffectiveTo)))
  THROW 51931,N'El mapping de IVA cambió respecto del plan revisado; no se sobrescribió.',1;

INSERT @InputVatUpgradeActions
SELECT reviewed.TenantId,reviewed.MappingId,reviewed.SourceAccountId,reviewed.TargetAccountId,reviewed.TargetCode,reviewed.TargetName
FROM @InputVatUpgradePlan reviewed
JOIN dbo.AccountingAccountMappings mapping WITH(UPDLOCK,HOLDLOCK)
  ON mapping.MappingId=reviewed.MappingId AND mapping.TenantId=reviewed.TenantId
  AND mapping.AccountId=reviewed.SourceAccountId AND mapping.Category=N'InputVat'
JOIN dbo.AccountingAccounts source WITH(UPDLOCK,HOLDLOCK)
  ON source.AccountId=reviewed.SourceAccountId AND source.TenantId=reviewed.TenantId
WHERE source.AccountType=N'Asset';

IF EXISTS(SELECT 1 FROM @InputVatUpgradeActions action
  JOIN dbo.AccountingAccounts source ON source.AccountId=action.SourceAccountId
  WHERE source.Code<>N'240810' OR source.Name COLLATE Latin1_General_100_BIN2<>N'IVA descontable'
    OR source.IsActive<>1 OR source.AllowsPosting<>1 OR source.RequiresParty<>0
    OR source.ParentAccountId IS NOT NULL OR source.FinancialFunction IS NOT NULL
    OR source.BalanceSide IS NOT NULL OR source.LegacyBusinessId IS NOT NULL
    OR source.BankAccountId IS NOT NULL OR source.PaymentMethodOptionId IS NOT NULL)
  THROW 51932,N'La cuenta origen difiere de la configuración histórica revisada; no se modificó.',1;

-- Compliance mappings belong to their explicit report definitions, not to the
-- posting category. Copying them would also change their signed interpretation
-- across Asset/Liability. Require a reviewed fiscal plan rather than guessing.
IF EXISTS(SELECT 1 FROM @InputVatUpgradeActions action
  JOIN compliance.ComplianceConceptMappings fiscal WITH(UPDLOCK,HOLDLOCK)
    ON fiscal.TenantId=action.TenantId AND fiscal.AccountId=action.SourceAccountId)
  THROW 51937,N'La cuenta IVA tiene mappings de cumplimiento; se requiere revisar explícitamente su destino fiscal antes de migrar.',1;

IF EXISTS(SELECT 1 FROM @InputVatUpgradeActions action
  JOIN dbo.AccountingAccounts existing WITH(UPDLOCK,HOLDLOCK)
    ON existing.AccountId=action.TargetAccountId OR (existing.TenantId=action.TenantId AND existing.Code=action.TargetCode)
  WHERE existing.AccountId<>action.TargetAccountId OR existing.TenantId<>action.TenantId
    OR existing.Code<>action.TargetCode OR existing.Name COLLATE Latin1_General_100_BIN2<>action.TargetName
    OR existing.AccountType<>N'Liability' OR existing.IsActive<>1 OR existing.AllowsPosting<>1 OR existing.RequiresParty<>0
    OR existing.ParentAccountId IS NOT NULL OR existing.FinancialFunction<>N'General' OR existing.FinancialFunction IS NULL
    OR existing.BalanceSide<>N'Debit' OR existing.BalanceSide IS NULL OR existing.LegacyBusinessId IS NOT NULL
    OR existing.BankAccountId IS NOT NULL OR existing.PaymentMethodOptionId IS NOT NULL)
  THROW 51933,N'El código o identidad de IVA destino ya tiene otra configuración; no se sobrescribió.',1;

DECLARE @InputVatUpgradeNow datetimeoffset(7)=SYSDATETIMEOFFSET();
INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,
  CreatedAt,FinancialFunction,BalanceSide)
SELECT DISTINCT action.TargetAccountId,action.TenantId,action.TargetCode,action.TargetName,N'Liability',1,0,1,
  @InputVatUpgradeNow,N'General',N'Debit'
FROM @InputVatUpgradeActions action
WHERE NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK) WHERE AccountId=action.TargetAccountId);
DECLARE @InputVatAccountsCreated int=@@ROWCOUNT;

IF EXISTS(SELECT 1 FROM @InputVatUpgradeActions action WHERE
  NOT EXISTS(SELECT 1 FROM dbo.Businesses business WHERE business.TenantId=action.TenantId AND business.IsActive=1)
  OR EXISTS(SELECT 1 FROM dbo.Businesses business WHERE business.TenantId=action.TenantId AND business.IsActive=1
    AND NOT EXISTS(SELECT 1 FROM accounting.PostingAccountEligibility(action.TenantId,N'InputVat',action.TargetAccountId))))
  THROW 51934,N'La cuenta IVA destino no satisface la política contable canónica.',1;

UPDATE mapping SET AccountId=action.TargetAccountId
FROM dbo.AccountingAccountMappings mapping
JOIN @InputVatUpgradeActions action ON action.MappingId=mapping.MappingId AND action.TenantId=mapping.TenantId
WHERE mapping.AccountId=action.SourceAccountId;
DECLARE @InputVatMappingsChanged int=@@ROWCOUNT;
IF @InputVatMappingsChanged<>(SELECT COUNT(*) FROM @InputVatUpgradeActions)
  THROW 51936,N'El mapping de IVA cambió durante la migración; no se aplicó parcialmente.',1;

-- Do not publish a successful upgrade with an active Ready business that will
-- fail posting. Unreviewed/custom incompatibilities stop here without guesses.
DECLARE @UnresolvedInputVatUpgrade nvarchar(max);
SELECT @UnresolvedInputVatUpgrade=STRING_AGG(CONVERT(nvarchar(max),CONCAT(
  CONVERT(nvarchar(36),unresolved.TenantId),N'/',CONVERT(nvarchar(36),unresolved.BusinessId),N': ',unresolved.Category)),N'; ')
FROM (SELECT TOP(12) business.TenantId,business.BusinessId,
    COALESCE(required.Category,N'Perfil obligatorio no disponible') Category
  FROM dbo.Businesses business
  JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId AND tenant.IsActive=1
  JOIN dbo.AccountingTenantSettings settings ON settings.TenantId=business.TenantId AND settings.Status=N'Ready'
  OUTER APPLY accounting.RequiredPostingAccountReadiness(business.TenantId,business.BusinessId,
    CASE WHEN settings.EffectiveFrom>CONVERT(date,SWITCHOFFSET(@InputVatUpgradeNow,'+00:00')) THEN settings.EffectiveFrom
      ELSE CONVERT(date,SWITCHOFFSET(@InputVatUpgradeNow,'+00:00')) END) required
  WHERE business.IsActive=1 AND required.ReadyAccountId IS NULL
  ORDER BY business.TenantId,business.BusinessId,required.Category) unresolved;
IF @UnresolvedInputVatUpgrade IS NOT NULL
BEGIN
  DECLARE @InputVatUpgradeError nvarchar(2048)=LEFT(CONCAT(
    N'Hay configuración contable incompatible sin resolver; se requiere un plan explícito. ',@UnresolvedInputVatUpgrade),2048);
  THROW 51935,@InputVatUpgradeError,1;
END;
COMMIT TRANSACTION;
PRINT CONCAT(N'Migración revisada de IVA: cuentas creadas=',@InputVatAccountsCreated,N'; mappings corregidos=',@InputVatMappingsChanged,N'.');
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
