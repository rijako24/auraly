-- Corrige únicamente los mapeos predeterminados que aún conservan el código y
-- nombre erróneos de la plantilla anterior. Los asientos y los mapeos propios
-- de cada sede permanecen intactos. La vigencia nueva empieza al día siguiente
-- del despliegue en Colombia para no cambiar la resolución de fechas ya usadas.
DECLARE @PucCutover date=DATEADD(day,1,CONVERT(date,SYSDATETIMEOFFSET() AT TIME ZONE 'SA Pacific Standard Time'));
DECLARE @PucNow datetimeoffset(7)=SYSDATETIMEOFFSET();
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

DECLARE @LegacyPuc table(Category nvarchar(64) PRIMARY KEY,Code nvarchar(32),Name nvarchar(200));
INSERT @LegacyPuc(Category,Code,Name) VALUES
  (N'DebitCardClearing',N'130510',N'Tarjetas débito por conciliar'),
  (N'CreditCardClearing',N'130515',N'Tarjetas crédito por conciliar'),
  (N'TransferClearing',N'130520',N'Transferencias por conciliar'),
  (N'CardClearing',N'130525',N'Tarjetas por conciliar'),
  (N'CashClosureDifferencesPending',N'139995',N'Diferencias de cierre pendientes de conciliación'),
  (N'SupplierCreditsReceivable',N'133595',N'Saldos a favor con proveedores'),
  (N'WithholdingIncomeTaxPayable',N'236540',N'Retención en la fuente por pagar'),
  (N'ServiceRevenue',N'415525',N'Ingresos por servicios de software'),
  (N'RoundingGain',N'429598',N'Ingresos por aproximaciones'),
  (N'RoundingLoss',N'539598',N'Gastos por aproximaciones'),
  (N'CashOverageIncome',N'429596',N'Sobrantes de caja'),
  (N'OperatingExpense',N'519510',N'Gastos operativos'),
  (N'PurchaseInsuranceExpense',N'513025',N'Seguros de mercancía'),
  (N'CustomsExpense',N'519525',N'Gastos legales y aduaneros'),
  (N'OtherPurchaseDirectExpense',N'519596',N'Otros costos directos de compra'),
  (N'CashShortageExpense',N'539596',N'Faltantes de caja'),
  (N'EmployerContributionsExpense',N'510568',N'Aportes patronales'),
  (N'BenefitsExpense',N'510530',N'Prestaciones sociales'),
  (N'EmployeeContributionsPayable',N'237005',N'Aportes del trabajador por pagar'),
  (N'ThirdPartyDeductionsPayable',N'238030',N'Deducciones de nómina por pagar'),
  (N'EmployerHealthPayable',N'237010',N'Salud del empleador por pagar'),
  (N'EmployerPensionPayable',N'237015',N'Pensión del empleador por pagar'),
  (N'BenefitsProvisionPayable',N'261005',N'Provisiones laborales'),
  (N'OccupationalRiskPayable',N'237020',N'Riesgos laborales por pagar'),
  (N'ParafiscalContributionsPayable',N'237025',N'Aportes parafiscales por pagar'),
  (N'DamagedInventoryExpense',N'529596',N'Pérdidas por averías y vencimientos'),
  (N'ConversionLossExpense',N'529597',N'Mermas de conversión'),
  (N'TransferLossExpense',N'529598',N'Faltantes en traslado'),
  (N'DispatchCashOverageIncome',N'429597',N'Sobrantes de transportadores'),
  (N'DispatchCashShortageExpense',N'539597',N'Faltantes de transportadores');

DECLARE @PucCutovers table(MappingId uniqueidentifier PRIMARY KEY,TenantId uniqueidentifier,
  Category nvarchar(64),NewAccountId uniqueidentifier);
INSERT @PucCutovers(MappingId,TenantId,Category,NewAccountId)
SELECT mapping.MappingId,mapping.TenantId,mapping.Category,replacement.AccountId
FROM dbo.AccountingAccountMappings mapping
JOIN @LegacyPuc legacy ON legacy.Category=mapping.Category
JOIN dbo.AccountingAccounts previous ON previous.AccountId=mapping.AccountId
  AND previous.TenantId=mapping.TenantId AND previous.Code=legacy.Code AND previous.Name=legacy.Name
JOIN dbo.AccountingConfigurationProfileAccounts definition ON definition.ProfileCode=N'AURALY_CO'
  AND definition.Category=legacy.Category
JOIN dbo.AccountingAccounts replacement ON replacement.TenantId=mapping.TenantId
  AND replacement.Code=definition.AccountCode AND replacement.Name=definition.AccountName
  AND replacement.AccountType=definition.AccountType AND replacement.IsActive=1 AND replacement.AllowsPosting=1
WHERE mapping.BusinessId IS NULL AND mapping.EffectiveFrom<@PucCutover
  AND mapping.EffectiveTo IS NULL
  AND NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings later
    WHERE later.TenantId=mapping.TenantId AND later.BusinessId IS NULL
      AND later.Category=mapping.Category AND later.MappingId<>mapping.MappingId);

UPDATE mapping SET EffectiveTo=DATEADD(day,-1,@PucCutover)
FROM dbo.AccountingAccountMappings mapping
JOIN @PucCutovers cutover ON cutover.MappingId=mapping.MappingId;

INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,
  AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
SELECT NEWID(),TenantId,NULL,Category,NewAccountId,@PucCutover,NULL,@PucNow
FROM @PucCutovers;

-- 240810 pertenece a la clase 2 aunque normalmente tenga saldo débito.
-- No se reclasifican balances históricos de una cuenta que ya recibió asientos.
UPDATE account SET AccountType=N'Liability'
FROM dbo.AccountingAccounts account
WHERE account.Code=N'240810' AND account.Name=N'IVA descontable'
  AND account.AccountType=N'Asset'
  AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntryLines line WHERE line.AccountId=account.AccountId)
  AND NOT EXISTS(SELECT 1 FROM dbo.AccountingOpeningBalanceLines line WHERE line.AccountId=account.AccountId);

-- Los conceptos de gasto guardan la cuenta directa; se actualizan únicamente
-- los conceptos de plantilla que nunca fueron editados por el tenant.
UPDATE concept SET ExpenseAccountId=replacement.AccountId,UpdatedAt=@PucNow
FROM dbo.ExpenseConcepts concept
JOIN dbo.Businesses business ON business.BusinessId=concept.BusinessId
JOIN dbo.AccountingConfigurationProfileExpenseConcepts template
  ON template.ProfileCode=N'AURALY_CO' AND template.Code=concept.Code
  AND template.Name=concept.Name
JOIN @LegacyPuc legacy ON legacy.Category=template.ExpenseAccountCategory
JOIN dbo.AccountingAccounts previous ON previous.AccountId=concept.ExpenseAccountId
  AND previous.TenantId=business.TenantId AND previous.Code=legacy.Code AND previous.Name=legacy.Name
JOIN dbo.AccountingConfigurationProfileAccounts definition
  ON definition.ProfileCode=template.ProfileCode AND definition.Category=legacy.Category
JOIN dbo.AccountingAccounts replacement ON replacement.TenantId=business.TenantId
  AND replacement.Code=definition.AccountCode AND replacement.Name=definition.AccountName
  AND replacement.AccountType=definition.AccountType
  AND replacement.IsActive=1 AND replacement.AllowsPosting=1
WHERE concept.CreatedAt=concept.UpdatedAt;

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
