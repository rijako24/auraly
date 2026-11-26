-- Explicitly approved deployment data, never a runtime compatibility rule.
-- Runs after the existing bank transition and before the common bootstrap.
IF N'$(DeploymentEnvironment)'=N'prod'
  AND EXISTS(SELECT 1 FROM dbo.Tenants WHERE TenantId='01a06318-d4bf-7f57-9776-4695b7833f7a')
BEGIN
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @PaymentUpgradeTenant uniqueidentifier='01a06318-d4bf-7f57-9776-4695b7833f7a',
  @PaymentUpgradeNow datetimeoffset(7)=SYSUTCDATETIME(),
  @PaymentUpgradeBankMapping uniqueidentifier='89b511d4-22af-44f9-ba30-3e3543fce827',
  @PaymentUpgradeLegacyBank uniqueidentifier='d3c0a3bc-b8b7-428f-9502-3bbecd1407a8',
  @PaymentUpgradePrimaryBank uniqueidentifier='182b6502-5d58-4746-bdea-cc010e7e2296';
DECLARE @ReviewedPaymentAccounts table(AccountId uniqueidentifier PRIMARY KEY,
  Code nvarchar(32),Name nvarchar(200),MethodCode nvarchar(32),FinancialFunction nvarchar(64));
INSERT @ReviewedPaymentAccounts VALUES
 ('bf06746d-1969-4778-8a75-babbe778a824',N'110505',N'Caja general',N'Cash',N'Cash'),
 ('de383cad-a363-48e5-b190-3dd768362ca8',N'13809501',N'Tarjetas débito por conciliar',N'DebitCard',N'CardClearing'),
 ('b652ffc3-d8f9-47f5-b085-ba0ae2ce5ab5',N'13809502',N'Tarjetas crédito por conciliar',N'CreditCard',N'CardClearing');

-- Complete only previously absent metadata, never change AccountType, code,
-- account identity, party requirements or a customer's explicit semantics.
IF EXISTS(SELECT 1 FROM @ReviewedPaymentAccounts reviewed
  LEFT JOIN dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK)
    ON account.TenantId=@PaymentUpgradeTenant AND account.AccountId=reviewed.AccountId
  WHERE account.AccountId IS NULL OR account.Code<>reviewed.Code
    OR account.Name COLLATE Latin1_General_100_BIN2<>reviewed.Name
    OR account.AccountType<>N'Asset' OR account.IsActive<>1 OR account.AllowsPosting<>1 OR account.RequiresParty<>0
    OR account.ParentAccountId IS NOT NULL OR account.LegacyBusinessId IS NOT NULL
    OR account.BankAccountId IS NOT NULL OR account.PaymentMethodOptionId IS NOT NULL
    OR (account.FinancialFunction IS NOT NULL AND account.FinancialFunction<>reviewed.FinancialFunction)
    OR (account.BalanceSide IS NOT NULL AND account.BalanceSide<>N'Debit'))
  THROW 51983,N'Las cuentas de pago difieren del plan revisado de Megafruver; no se sobrescribió su configuración.',1;
UPDATE account SET FinancialFunction=reviewed.FinancialFunction,BalanceSide=N'Debit'
FROM dbo.AccountingAccounts account JOIN @ReviewedPaymentAccounts reviewed ON reviewed.AccountId=account.AccountId
WHERE account.TenantId=@PaymentUpgradeTenant AND (account.FinancialFunction IS NULL OR account.BalanceSide IS NULL);

DECLARE @ReviewedPaymentBanks table(BankAccountId uniqueidentifier PRIMARY KEY);
INSERT @ReviewedPaymentBanks VALUES(@PaymentUpgradePrimaryBank),('2f2f8b6b-d33c-4dfb-a991-238898d5b3c3');
DECLARE @PaymentUpgradeBankAccounts table(BankAccountId uniqueidentifier PRIMARY KEY,AccountId uniqueidentifier,
  AccountCode nvarchar(32),DisplayName nvarchar(160),IsPrimary bit);
INSERT @PaymentUpgradeBankAccounts
SELECT bank.BankAccountId,selected.AccountId,account.Code,LEFT(bank.DisplayName,160),bank.IsPrimary
FROM @ReviewedPaymentBanks reviewed
JOIN accounting.BankAccounts bank WITH(UPDLOCK,HOLDLOCK)
  ON bank.TenantId=@PaymentUpgradeTenant AND bank.BankAccountId=reviewed.BankAccountId
CROSS APPLY accounting.BankPostingAccount(@PaymentUpgradeTenant,bank.BankAccountId) selected
JOIN dbo.AccountingAccounts account ON account.TenantId=@PaymentUpgradeTenant AND account.AccountId=selected.AccountId
WHERE bank.IsActive=1 AND selected.CandidateCount=1
  AND bank.HistoricalAccountingAccountId=@PaymentUpgradeLegacyBank;
IF (SELECT COUNT(*) FROM @PaymentUpgradeBankAccounts)<>2 OR NOT EXISTS(
  SELECT 1 FROM @PaymentUpgradeBankAccounts WHERE BankAccountId=@PaymentUpgradePrimaryBank AND IsPrimary=1)
  THROW 51984,N'Los bancos reales difieren del corte revisado de Megafruver; se requiere revisar sus auxiliares.',1;

-- The old shared bank auxiliary remains historical. Retarget only the exact
-- reviewed general mapping to the already designated primary bank's auxiliary.
IF EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings WITH(UPDLOCK,HOLDLOCK)
  WHERE TenantId=@PaymentUpgradeTenant AND MappingId=@PaymentUpgradeBankMapping AND AccountId=@PaymentUpgradeLegacyBank)
BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings WHERE TenantId=@PaymentUpgradeTenant
    AND MappingId=@PaymentUpgradeBankMapping AND Category=N'Bank' AND BusinessId IS NULL
    AND EffectiveFrom=CONVERT(date,'20260101') AND EffectiveTo IS NULL)
    THROW 51985,N'El mapping bancario cambió respecto del plan revisado de Megafruver.',1;
  UPDATE mapping SET AccountId=bank.AccountId FROM dbo.AccountingAccountMappings mapping
  JOIN @PaymentUpgradeBankAccounts bank ON bank.BankAccountId=@PaymentUpgradePrimaryBank
  WHERE mapping.TenantId=@PaymentUpgradeTenant AND mapping.MappingId=@PaymentUpgradeBankMapping;
END;

DECLARE @PaymentUpgradeCandidates table(MethodCode nvarchar(32),AccountId uniqueidentifier,
  Code nvarchar(64),Name nvarchar(160));
INSERT @PaymentUpgradeCandidates
SELECT MethodCode,AccountId,CONCAT(MethodCode,N'-',Code),LEFT(Name,160) FROM @ReviewedPaymentAccounts
UNION ALL SELECT N'Transfer',AccountId,CONCAT(N'Transfer-',AccountCode),DisplayName FROM @PaymentUpgradeBankAccounts;
IF EXISTS(SELECT 1 FROM @PaymentUpgradeCandidates candidate
  JOIN reference.PaymentMethods existing WITH(UPDLOCK,HOLDLOCK)
    ON existing.TenantId=@PaymentUpgradeTenant AND existing.Code=candidate.Code
  JOIN reference.Options type ON type.OptionId=existing.PaymentTypeId
  WHERE existing.AccountId<>candidate.AccountId OR type.Code<>candidate.MethodCode)
  THROW 51986,N'Un código de forma de pago del plan ya pertenece a otra configuración; no se sobrescribió.',1;

DECLARE @PaymentUpgradeCreated table(PaymentMethodId uniqueidentifier PRIMARY KEY);
INSERT reference.PaymentMethods(TenantId,PaymentMethodId,Code,Name,PaymentTypeId,AccountId,BankAccountId,
  UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,UseForVouchers,IsActive,SortOrder,Version,CreatedAt,UpdatedAt)
OUTPUT inserted.PaymentMethodId INTO @PaymentUpgradeCreated
SELECT @PaymentUpgradeTenant,NEWID(),candidate.Code,candidate.Name,type.OptionId,candidate.AccountId,
  CASE WHEN type.Code=N'Transfer' THEN account.BankAccountId END,
  template.UseForSales,template.UseForReceipts,template.UseForSupplierPayments,template.UseForPayroll,
  template.UseForVouchers,1,template.SortOrder,1,@PaymentUpgradeNow,@PaymentUpgradeNow
FROM @PaymentUpgradeCandidates candidate
JOIN dbo.AccountingAccounts account ON account.AccountId=candidate.AccountId AND account.TenantId=@PaymentUpgradeTenant
JOIN reference.Options type ON type.CatalogCode=N'payment-method' AND type.Code=candidate.MethodCode AND type.IsActive=1
JOIN dbo.AccountingConfigurationProfiles profile ON profile.IsDefault=1 AND profile.IsActive=1
JOIN dbo.AccountingConfigurationProfilePaymentMethods template
  ON template.ProfileCode=profile.ProfileCode AND template.PaymentTypeId=type.OptionId
WHERE NOT EXISTS(SELECT 1 FROM reference.PaymentMethods existing WITH(UPDLOCK,HOLDLOCK)
  WHERE existing.TenantId=@PaymentUpgradeTenant AND existing.PaymentTypeId=type.OptionId);

IF EXISTS(SELECT 1 FROM @PaymentUpgradeCandidates candidate WHERE NOT EXISTS(
    SELECT 1 FROM reference.PaymentMethodDetails form WHERE form.TenantId=@PaymentUpgradeTenant
      AND form.MethodCode=candidate.MethodCode AND form.IsActive=1 AND form.IsReady=1))
  OR EXISTS(SELECT 1 FROM dbo.Businesses business
    CROSS APPLY accounting.RequiredPostingAccountReadiness(@PaymentUpgradeTenant,business.BusinessId,
      CONVERT(date,@PaymentUpgradeNow)) required
    WHERE business.TenantId=@PaymentUpgradeTenant AND business.IsActive=1 AND required.ReadyAccountId IS NULL)
  THROW 51987,N'La configuración revisada no dejó todos los tipos y cuentas requeridos disponibles; revisa la configuración propia.',1;

IF EXISTS(SELECT 1 FROM @PaymentUpgradeCreated)
  INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
  SELECT NEWID(),business.BusinessId,N'Configuration',COALESCE(latest.CursorValue,0)+1,@PaymentUpgradeNow
  FROM dbo.Businesses business OUTER APPLY(
    SELECT MAX(AvailableThroughCursor) CursorValue FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=business.BusinessId AND Stream=N'Configuration') latest
  WHERE business.TenantId=@PaymentUpgradeTenant;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
END;
