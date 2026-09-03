SET NOCOUNT ON;

DECLARE @AccountingNow datetimeoffset=SYSDATETIMEOFFSET();
IF NOT EXISTS(SELECT 1 FROM dbo.AccountingConfigurationProfiles WHERE ProfileCode=N'AURALY_CO')
  INSERT dbo.AccountingConfigurationProfiles(ProfileCode,Name,IsDefault,IsActive)
  VALUES(N'AURALY_CO',N'Auraly Colombia',1,1);

MERGE dbo.AccountingConfigurationProfileAccounts AS target
USING (VALUES
  (N'Cash',N'Caja',N'110505',N'Caja general',N'Asset',0,1),
  (N'Bank',N'Bancos',N'111005',N'Bancos nacionales',N'Asset',0,2),
  (N'DebitCardClearing',N'Tarjeta débito por conciliar',N'130510',N'Tarjetas débito por conciliar',N'Asset',0,3),
  (N'CreditCardClearing',N'Tarjeta crédito por conciliar',N'130515',N'Tarjetas crédito por conciliar',N'Asset',0,4),
  (N'TransferClearing',N'Transferencias por conciliar',N'130520',N'Transferencias por conciliar',N'Asset',0,5),
  (N'CardClearing',N'Tarjetas por conciliar',N'130525',N'Tarjetas por conciliar',N'Asset',0,50),
  (N'CashClosureDifferencesPending',N'Diferencias de cierre pendientes',N'139995',N'Diferencias de cierre pendientes de conciliación',N'Asset',0,51),
  (N'AccountsReceivable',N'Clientes',N'130505',N'Clientes nacionales',N'Asset',1,6),
  (N'SupplierCreditsReceivable',N'Saldos a favor con proveedores',N'133595',N'Saldos a favor con proveedores',N'Asset',1,7),
  (N'WithholdingIncomeTaxReceivable',N'Retefuente a favor',N'135515',N'Retención en la fuente a favor',N'Asset',0,8),
  (N'WithholdingVatReceivable',N'ReteIVA a favor',N'135517',N'Retención de IVA a favor',N'Asset',0,9),
  (N'WithholdingIcaReceivable',N'ReteICA a favor',N'135518',N'Retención de ICA a favor',N'Asset',0,10),
  (N'Inventory',N'Inventarios',N'143505',N'Inventarios de mercancías',N'Asset',0,11),
  (N'AccountsPayable',N'Proveedores',N'220505',N'Proveedores nacionales',N'Liability',1,12),
  (N'OutputVat',N'IVA generado',N'240805',N'IVA generado',N'Liability',0,13),
  (N'InputVat',N'IVA descontable',N'240810',N'IVA descontable',N'Asset',0,14),
  (N'WithholdingIncomeTaxPayable',N'Retefuente por pagar',N'236540',N'Retención en la fuente por pagar',N'Liability',0,15),
  (N'WithholdingVatPayable',N'ReteIVA por pagar',N'236701',N'Retención de IVA por pagar',N'Liability',0,16),
  (N'WithholdingIcaPayable',N'ReteICA por pagar',N'236805',N'Retención de ICA por pagar',N'Liability',0,17),
  (N'CustomerCreditsPayable',N'Saldos a favor de clientes',N'238095',N'Saldos a favor de clientes',N'Liability',1,18),
  (N'OwnerContributions',N'Aportes del propietario',N'311505',N'Aportes del propietario',N'Equity',0,19),
  (N'SalesRevenue',N'Ingresos por ventas',N'413595',N'Ingresos por venta de mercancías',N'Revenue',0,20),
  (N'ServiceRevenue',N'Ingresos por servicios',N'415525',N'Ingresos por servicios de software',N'Revenue',0,52),
  (N'SalesReturns',N'Devoluciones en ventas',N'417595',N'Devoluciones en ventas',N'ContraRevenue',0,21),
  (N'OtherIncome',N'Otros ingresos de caja',N'429595',N'Otros ingresos',N'Revenue',0,22),
  (N'CashOverageIncome',N'Sobrantes de caja',N'429596',N'Sobrantes de caja',N'Revenue',0,23),
  (N'OperatingExpense',N'Gastos operativos',N'519510',N'Gastos operativos',N'Expense',0,24),
  (N'PurchasesExpense',N'Compras no inventariables',N'519595',N'Compras no inventariables',N'Expense',0,25),
  (N'PurchaseFreightExpense',N'Fletes de compra no capitalizados',N'513550',N'Transporte, fletes y acarreos',N'Expense',0,53),
  (N'PurchaseInsuranceExpense',N'Seguros de compra no capitalizados',N'513025',N'Seguros de mercancía',N'Expense',0,54),
  (N'CustomsExpense',N'Gastos aduaneros no capitalizados',N'519525',N'Gastos legales y aduaneros',N'Expense',0,55),
  (N'PurchaseHandlingExpense',N'Manejo de compra no capitalizado',N'513595',N'Manejo y logística',N'Expense',0,56),
  (N'OtherPurchaseDirectExpense',N'Otros costos directos no capitalizados',N'519596',N'Otros costos directos de compra',N'Expense',0,57),
  (N'OtherExpense',N'Otras salidas de caja',N'539595',N'Otras salidas de caja',N'Expense',0,26),
  (N'CashShortageExpense',N'Faltantes de caja',N'539596',N'Faltantes de caja',N'Expense',0,27),
  (N'CostOfGoodsSold',N'Costo de ventas',N'613595',N'Costo de mercancía vendida',N'Expense',0,28),
  (N'SalaryExpense',N'Sueldos y salarios',N'510506',N'Sueldos y salarios',N'Expense',1,29),
  (N'VariableEarningsExpense',N'Devengos variables',N'510515',N'Horas extras y recargos',N'Expense',1,30),
  (N'TransportAllowanceExpense',N'Auxilio de transporte',N'510527',N'Auxilio de transporte',N'Expense',1,31),
  (N'EmployerContributionsExpense',N'Aportes patronales',N'510568',N'Aportes patronales',N'Expense',1,32),
  (N'BenefitsExpense',N'Prestaciones sociales',N'510530',N'Prestaciones sociales',N'Expense',1,33),
  (N'EmployeeContributionsPayable',N'Aportes del trabajador por pagar',N'237005',N'Aportes del trabajador por pagar',N'Liability',1,34),
  (N'PayrollWithholdingPayable',N'Retención laboral por pagar',N'236505',N'Retención laboral por pagar',N'Liability',1,35),
  (N'ThirdPartyDeductionsPayable',N'Deducciones a terceros por pagar',N'238030',N'Deducciones de nómina por pagar',N'Liability',1,36),
  (N'NetPayrollPayable',N'Nómina por pagar',N'250505',N'Salarios por pagar',N'Liability',1,37),
  (N'BenefitsProvisionPayable',N'Provisiones laborales por pagar',N'261005',N'Provisiones laborales',N'Liability',1,38),
  (N'EmployeeLoansReceivable',N'Préstamos a empleados',N'136595',N'Préstamos a trabajadores',N'Asset',1,39),
  (N'EmployerHealthPayable',N'Salud del empleador por pagar',N'237010',N'Salud del empleador por pagar',N'Liability',1,40),
  (N'EmployerPensionPayable',N'Pensión del empleador por pagar',N'237015',N'Pensión del empleador por pagar',N'Liability',1,41),
  (N'OccupationalRiskPayable',N'Riesgos laborales por pagar',N'237020',N'Riesgos laborales por pagar',N'Liability',1,42),
  (N'ParafiscalContributionsPayable',N'Parafiscales por pagar',N'237025',N'Aportes parafiscales por pagar',N'Liability',1,43),
  (N'InventoryDifferences',N'Diferencias de inventario',N'529595',N'Diferencias de inventario',N'Expense',0,44),
  (N'DamagedInventoryExpense',N'Pérdidas por averías y vencimientos',N'529596',N'Pérdidas por averías y vencimientos',N'Expense',0,45),
  (N'ConversionLossExpense',N'Mermas de conversión',N'529597',N'Mermas de conversión',N'Expense',0,46),
  (N'TransferLossExpense',N'Faltantes en traslado',N'529598',N'Faltantes en traslado',N'Expense',0,47),
  (N'DispatchCashOverageIncome',N'Sobrantes de transportadores',N'429597',N'Sobrantes de transportadores',N'Revenue',0,48),
  (N'DispatchCashShortageExpense',N'Faltantes de transportadores',N'539597',N'Faltantes de transportadores',N'Expense',0,49)
) AS source(Category,DisplayName,AccountCode,AccountName,AccountType,RequiresParty,DisplayOrder)
ON target.ProfileCode=N'AURALY_CO' AND target.Category=source.Category
WHEN MATCHED THEN UPDATE SET DisplayName=source.DisplayName,AccountCode=source.AccountCode,
  AccountName=source.AccountName,AccountType=source.AccountType,RequiresParty=source.RequiresParty,
  DisplayOrder=source.DisplayOrder
WHEN NOT MATCHED THEN INSERT(ProfileCode,Category,DisplayName,AccountCode,AccountName,AccountType,AllowsPosting,RequiresParty,IsRequired,DisplayOrder)
  VALUES(N'AURALY_CO',source.Category,source.DisplayName,source.AccountCode,source.AccountName,source.AccountType,1,source.RequiresParty,1,source.DisplayOrder);

MERGE dbo.AccountingConfigurationProfileExpenseConcepts AS target
USING (VALUES
  (N'PEAJE',N'Peajes',N'OperatingExpense',10),
  (N'PARQUEADERO',N'Parqueaderos',N'OperatingExpense',20),
  (N'COMBUSTIBLE',N'Combustible',N'OperatingExpense',30),
  (N'TRANSPORTE',N'Transporte y mensajería',N'OperatingExpense',40),
  (N'SERVICIOS',N'Servicios operativos',N'OperatingExpense',50),
  (N'OTROS',N'Otros gastos',N'OtherExpense',60)
) AS source(Code,Name,ExpenseAccountCategory,DisplayOrder)
ON target.ProfileCode=N'AURALY_CO' AND target.Code=source.Code
WHEN MATCHED THEN UPDATE SET Name=source.Name,ExpenseAccountCategory=source.ExpenseAccountCategory,
  DisplayOrder=source.DisplayOrder,IsActive=1
WHEN NOT MATCHED THEN INSERT(ProfileCode,Code,Name,ExpenseAccountCategory,DisplayOrder,IsActive)
  VALUES(N'AURALY_CO',source.Code,source.Name,source.ExpenseAccountCategory,source.DisplayOrder,1);

MERGE dbo.AccountingSourceCategoryMappings AS target
USING (VALUES
  (N'PosPaymentMethod',N'Cash',N'Cash'),
  (N'PosPaymentMethod',N'DebitCard',N'DebitCardClearing'),
  (N'PosPaymentMethod',N'CreditCard',N'CreditCardClearing'),
  (N'PosPaymentMethod',N'Transfer',N'TransferClearing'),
  (N'PosPaymentMethod',N'Credit',N'AccountsReceivable'),
  (N'ClosurePaymentMethod',N'Cash',N'Cash'),
  (N'ClosurePaymentMethod',N'Card',N'CardClearing'),
  (N'ClosurePaymentMethod',N'Transfer',N'TransferClearing'),
  (N'SupplierPaymentMethod',N'Cash',N'Cash'),
  (N'SupplierPaymentMethod',N'BankTransfer',N'Bank'),
  (N'CustomerPaymentMethod',N'Cash',N'Cash'),
  (N'CustomerPaymentMethod',N'BankTransfer',N'Bank'),
  (N'CustomerPaymentMethod',N'DebitCard',N'DebitCardClearing'),
  (N'CustomerPaymentMethod',N'CreditCard',N'CreditCardClearing'),
  (N'PurchaseWithholdingKind',N'IncomeTax',N'WithholdingIncomeTaxPayable'),
  (N'PurchaseWithholdingKind',N'Vat',N'WithholdingVatPayable'),
  (N'PurchaseWithholdingKind',N'IndustryCommerce',N'WithholdingIcaPayable'),
  (N'PurchaseCostKind',N'Freight',N'PurchaseFreightExpense'),
  (N'PurchaseCostKind',N'Insurance',N'PurchaseInsuranceExpense'),
  (N'PurchaseCostKind',N'CustomsDuty',N'CustomsExpense'),
  (N'PurchaseCostKind',N'CustomsBrokerage',N'CustomsExpense'),
  (N'PurchaseCostKind',N'Handling',N'PurchaseHandlingExpense'),
  (N'PurchaseCostKind',N'OtherDirectCost',N'OtherPurchaseDirectExpense'),
  (N'PurchaseCostKind',N'ImportVat',N'CustomsExpense'),
  (N'SaleWithholdingKind',N'IncomeTax',N'WithholdingIncomeTaxReceivable'),
  (N'SaleWithholdingKind',N'Vat',N'WithholdingVatReceivable'),
  (N'SaleWithholdingKind',N'IndustryCommerce',N'WithholdingIcaReceivable')
) AS source(SourceType,SourceCode,Category)
ON target.ProfileCode=N'AURALY_CO' AND target.SourceType=source.SourceType AND target.SourceCode=source.SourceCode
WHEN MATCHED THEN UPDATE SET Category=source.Category
WHEN NOT MATCHED THEN INSERT(ProfileCode,SourceType,SourceCode,Category)
  VALUES(N'AURALY_CO',source.SourceType,source.SourceCode,source.Category)
WHEN NOT MATCHED BY SOURCE
  AND target.ProfileCode=N'AURALY_CO'
  AND target.SourceType IN(N'PosPaymentMethod',N'ClosurePaymentMethod')
THEN DELETE;

MERGE dbo.ReasonTemplates AS target
USING (VALUES
  (N'CashIn',N'OTHER_INCOME',N'Otros ingresos de caja',N'In',N'OtherIncome',1,10),
  (N'CashIn',N'OWNER_CONTRIBUTION',N'Aporte del propietario',N'In',N'OwnerContributions',1,20),
  (N'CashOut',N'OPERATING_EXPENSE',N'Gasto operativo',N'Out',N'OperatingExpense',1,10),
  (N'CashOut',N'CASH_TO_BANK',N'Consignación a banco',N'Out',N'Bank',1,20),
  (N'CashOut',N'OTHER_OUTFLOW',N'Otra salida de caja',N'Out',N'OtherExpense',1,30),
  (N'StockCount',N'PHYSICAL_COUNT',N'Conteo físico programado',NULL,N'InventoryDifferences',0,10),
  (N'StockCount',N'INVENTORY_VERIFICATION',N'Verificación de existencias',NULL,N'InventoryDifferences',0,20),
  (N'InventoryAdjustment',N'MANUAL_ADJUSTMENT',N'Corrección de saldo',NULL,N'InventoryDifferences',0,10),
  (N'InventoryAdjustment',N'INITIAL_BALANCE',N'Saldo inicial',NULL,N'InventoryDifferences',0,20),
  (N'InventoryAdjustment',N'FOUND_SURPLUS',N'Sobrante identificado',NULL,N'InventoryDifferences',0,30),
  (N'InventoryAdjustment',N'FOUND_SHORTAGE',N'Faltante identificado',NULL,N'InventoryDifferences',0,40),
  (N'WarehouseTransfer',N'WAREHOUSE_TRANSFER',N'Reabastecimiento entre bodegas',NULL,NULL,0,10),
  (N'WarehouseTransfer',N'STOCK_REDISTRIBUTION',N'Redistribución de existencias',NULL,NULL,0,20),
  (N'WarehouseTransfer',N'TRANSFER_SHORTAGE',N'Faltante definitivo en traslado',NULL,N'TransferLossExpense',1,30),
  (N'ProductConversion',N'PRESENTATION_CHANGE',N'Cambio de presentación',NULL,N'ConversionLossExpense',0,10),
  (N'Damage',N'DAMAGE',N'Producto averiado',NULL,N'DamagedInventoryExpense',0,10),
  (N'Damage',N'EXPIRED',N'Producto vencido',NULL,N'DamagedInventoryExpense',0,20),
  (N'Damage',N'NOT_SALEABLE',N'Producto no vendible',NULL,N'DamagedInventoryExpense',0,30),
  (N'SalesReturn',N'CustomerChangedMind',N'El cliente cambió de opinión',NULL,NULL,0,10),
  (N'SalesReturn',N'WrongProduct',N'Producto equivocado',NULL,NULL,0,20),
  (N'SalesReturn',N'QualityIssue',N'Problema de calidad',NULL,NULL,0,30),
  (N'SalesReturn',N'Damaged',N'Producto averiado',NULL,NULL,0,40),
  (N'SalesReturn',N'BillingCorrection',N'Corrección de facturación',NULL,NULL,0,50),
  (N'SalesReturn',N'Other',N'Otro motivo',NULL,NULL,0,60),
  (N'PurchaseReturn',N'WrongProduct',N'Producto equivocado',NULL,NULL,0,10),
  (N'PurchaseReturn',N'ExcessQuantity',N'Cantidad excedente',NULL,NULL,0,20),
  (N'PurchaseReturn',N'QualityIssue',N'Problema de calidad',NULL,NULL,0,30),
  (N'PurchaseReturn',N'Damaged',N'Producto averiado',NULL,NULL,0,40),
  (N'PurchaseReturn',N'CommercialAgreement',N'Acuerdo comercial',NULL,NULL,0,50),
  (N'PurchaseReturn',N'ReceiptCorrection',N'Corrección de recepción',NULL,NULL,0,60),
  (N'NotDelivered',N'CUSTOMER_ABSENT',N'Cliente ausente',NULL,NULL,0,10),
  (N'NotDelivered',N'BUSINESS_CLOSED',N'Local cerrado',NULL,NULL,0,20),
  (N'NotDelivered',N'CUSTOMER_REJECTED',N'Cliente rechazó el pedido',NULL,NULL,0,30),
  (N'NotDelivered',N'WRONG_ADDRESS',N'Dirección incorrecta',NULL,NULL,0,40),
  (N'NotDelivered',N'NO_PAYMENT',N'Cliente sin medio de pago',NULL,NULL,0,50),
  (N'NotDelivered',N'ACCESS_RESTRICTED',N'No fue posible acceder al lugar',NULL,NULL,0,60),
  (N'NotDelivered',N'OTHER',N'Otro motivo',NULL,NULL,0,999),
  (N'DeliveryReturn',N'CUSTOMER_RETURN',N'Devolución solicitada por el cliente',NULL,NULL,0,10),
  (N'DeliveryReturn',N'WRONG_PRODUCT',N'Producto equivocado',NULL,NULL,0,20),
  (N'DeliveryReturn',N'DAMAGED_DELIVERY',N'Producto averiado durante la entrega',NULL,NULL,0,30),
  (N'DeliveryReturn',N'QUALITY_ISSUE',N'Problema de calidad',NULL,NULL,0,40),
  (N'DeliveryReturn',N'OTHER',N'Otro motivo',NULL,NULL,0,999)
) AS source(ReasonType,Code,Name,Direction,CounterpartCategory,RequiresReference,DisplayOrder)
ON target.ProfileCode=N'AURALY_CO' AND target.ReasonType=source.ReasonType AND target.Code=source.Code
WHEN MATCHED THEN UPDATE SET Name=source.Name,Direction=source.Direction,
  CounterpartAccountingCategory=source.CounterpartCategory,RequiresReference=source.RequiresReference,
  DisplayOrder=source.DisplayOrder,IsActive=1
WHEN NOT MATCHED THEN INSERT(ProfileCode,ReasonType,Code,Name,Direction,CounterpartAccountingCategory,RequiresReference,DisplayOrder,IsActive)
  VALUES(N'AURALY_CO',source.ReasonType,source.Code,source.Name,source.Direction,source.CounterpartCategory,source.RequiresReference,source.DisplayOrder,1);

INSERT dbo.AccountingAccounts(
  AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
SELECT NEWID(),t.TenantId,d.AccountCode,d.AccountName,d.AccountType,d.AllowsPosting,d.RequiresParty,1,@AccountingNow
FROM dbo.Tenants t
CROSS JOIN dbo.AccountingConfigurationProfiles p
INNER JOIN dbo.AccountingConfigurationProfileAccounts d ON d.ProfileCode=p.ProfileCode
WHERE t.IsActive=1 AND NOT EXISTS(
  SELECT 1 FROM dbo.AccountingAccounts a WHERE a.TenantId=t.TenantId AND a.Code=d.AccountCode)
  AND p.IsDefault=1 AND p.IsActive=1;

INSERT dbo.AccountingAccountMappings(
  MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
SELECT NEWID(),t.TenantId,NULL,d.Category,a.AccountId,CONVERT(date,'20000101'),NULL,@AccountingNow
FROM dbo.Tenants t
CROSS JOIN dbo.AccountingConfigurationProfiles p
INNER JOIN dbo.AccountingConfigurationProfileAccounts d ON d.ProfileCode=p.ProfileCode
INNER JOIN dbo.AccountingAccounts a ON a.TenantId=t.TenantId AND a.Code=d.AccountCode
WHERE t.IsActive=1 AND a.IsActive=1 AND a.AllowsPosting=1 AND NOT EXISTS(
  SELECT 1 FROM dbo.AccountingAccountMappings m
  WHERE m.TenantId=t.TenantId AND m.BusinessId IS NULL AND m.Category=d.Category)
  AND p.IsDefault=1 AND p.IsActive=1;

INSERT dbo.AccountingCostCenters(
  CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
SELECT NEWID(),b.BusinessId,N'GENERAL',N'Operación general',NULL,1,1,@AccountingNow
FROM dbo.Businesses b
WHERE b.IsActive=1 AND NOT EXISTS(
  SELECT 1 FROM dbo.AccountingCostCenters c
  WHERE c.BusinessId=b.BusinessId AND c.IsDefault=1 AND c.IsActive=1);

INSERT dbo.BusinessReasons(
  ReasonId,BusinessId,ReasonType,Code,Name,Direction,CounterpartAccountingCategory,
  DefaultCostCenterId,RequiresReference,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
SELECT COALESCE(c.ReasonId,NEWID()),b.BusinessId,t.ReasonType,t.Code,t.Name,t.Direction,t.CounterpartAccountingCategory,
       NULL,t.RequiresReference,1,1,t.DisplayOrder,@AccountingNow,@AccountingNow
FROM dbo.Businesses b
CROSS JOIN dbo.AccountingConfigurationProfiles p
INNER JOIN dbo.ReasonTemplates t ON t.ProfileCode=p.ProfileCode
LEFT JOIN dbo.CashMovementReasons c ON c.BusinessId=b.BusinessId AND c.Code=t.Code
  AND c.Direction=t.Direction AND t.ReasonType IN (N'CashIn',N'CashOut')
WHERE b.IsActive=1 AND p.IsDefault=1 AND p.IsActive=1 AND t.IsActive=1
  AND NOT EXISTS(SELECT 1 FROM dbo.BusinessReasons r
    WHERE r.BusinessId=b.BusinessId AND r.ReasonType=t.ReasonType AND r.Code=t.Code);

UPDATE reason SET CounterpartAccountingCategory=template.CounterpartAccountingCategory,
                  UpdatedAt=@AccountingNow
FROM dbo.BusinessReasons reason
INNER JOIN dbo.Businesses business ON business.BusinessId=reason.BusinessId
CROSS JOIN dbo.AccountingConfigurationProfiles profile
INNER JOIN dbo.ReasonTemplates template
  ON template.ProfileCode=profile.ProfileCode
 AND template.ReasonType=reason.ReasonType AND template.Code=reason.Code
WHERE business.IsActive=1 AND profile.IsDefault=1 AND profile.IsActive=1
  AND template.IsActive=1 AND reason.CounterpartAccountingCategory IS NULL
  AND template.CounterpartAccountingCategory IS NOT NULL;

INSERT dbo.BusinessReasons(
  ReasonId,BusinessId,ReasonType,Code,Name,Direction,CounterpartAccountingCategory,
  DefaultCostCenterId,RequiresReference,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
SELECT i.InventoryReasonId,i.BusinessId,i.OperationType,i.Code,i.Name,NULL,NULL,NULL,0,
       i.IsSystem,i.IsActive,i.DisplayOrder,i.CreatedAt,i.UpdatedAt
FROM dbo.InventoryReasons i
WHERE NOT EXISTS(SELECT 1 FROM dbo.BusinessReasons r
  WHERE r.BusinessId=i.BusinessId AND r.ReasonType=i.OperationType AND r.Code=i.Code);

INSERT dbo.BusinessReasons(
  ReasonId,BusinessId,ReasonType,Code,Name,Direction,CounterpartAccountingCategory,
  DefaultCostCenterId,RequiresReference,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
SELECT c.ReasonId,c.BusinessId,CASE WHEN c.Direction=N'In' THEN N'CashIn' ELSE N'CashOut' END,
       c.Code,c.Name,c.Direction,c.CounterpartAccountingCategory,c.DefaultCostCenterId,
       c.RequiresReference,0,c.IsActive,0,c.CreatedAt,c.UpdatedAt
FROM dbo.CashMovementReasons c
WHERE NOT EXISTS(SELECT 1 FROM dbo.BusinessReasons r
  WHERE r.BusinessId=c.BusinessId
    AND r.ReasonType=CASE WHEN c.Direction=N'In' THEN N'CashIn' ELSE N'CashOut' END
    AND r.Code=c.Code);

DECLARE @AccountingYearStart date=DATEFROMPARTS(YEAR(@AccountingNow),1,1);
DECLARE @AccountingYearEnd date=DATEFROMPARTS(YEAR(@AccountingNow),12,31);
INSERT dbo.AccountingPeriods(PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
SELECT NEWID(),t.TenantId,CONVERT(nvarchar(4),YEAR(@AccountingNow)),
       @AccountingYearStart,@AccountingYearEnd,N'Open',@AccountingNow
FROM dbo.Tenants t
WHERE t.IsActive=1 AND NOT EXISTS(
  SELECT 1 FROM dbo.AccountingPeriods p
  WHERE p.TenantId=t.TenantId
    AND p.StartsOn<=@AccountingYearEnd AND p.EndsOn>=@AccountingYearStart);
