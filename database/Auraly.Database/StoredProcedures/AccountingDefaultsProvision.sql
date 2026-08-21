CREATE PROCEDURE dbo.AccountingDefaultsProvision
    @TenantId uniqueidentifier,
    @BusinessId uniqueidentifier,
    @Now datetimeoffset(7)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
        THROW 51041,'La sede no pertenece al tenant indicado.',1;

    DECLARE @Accounts TABLE(
        Category nvarchar(64) NOT NULL,
        Code nvarchar(32) NOT NULL,
        Name nvarchar(200) NOT NULL,
        AccountType nvarchar(24) NOT NULL,
        RequiresParty bit NOT NULL);
    INSERT @Accounts(Category,Code,Name,AccountType,RequiresParty) VALUES
      (N'Cash',N'110505',N'Caja general',N'Asset',0),
      (N'Bank',N'111005',N'Bancos moneda nacional',N'Asset',0),
      (N'DebitCardClearing',N'111010',N'Tarjetas débito por cobrar',N'Asset',0),
      (N'CreditCardClearing',N'111015',N'Tarjetas crédito por cobrar',N'Asset',0),
      (N'TransferClearing',N'111020',N'Transferencias por conciliar',N'Asset',0),
      (N'AccountsReceivable',N'130505',N'Clientes nacionales',N'Asset',1),
      (N'SupplierCreditsReceivable',N'133595',N'Saldos a favor con proveedores',N'Asset',1),
      (N'WithholdingIncomeTaxReceivable',N'135515',N'Retención en la fuente a favor',N'Asset',0),
      (N'WithholdingVatReceivable',N'135517',N'Retención de IVA a favor',N'Asset',0),
      (N'WithholdingIcaReceivable',N'135518',N'Retención de ICA a favor',N'Asset',0),
      (N'Inventory',N'143505',N'Inventarios de mercancías',N'Asset',0),
      (N'AccountsPayable',N'220505',N'Proveedores nacionales',N'Liability',1),
      (N'CustomerCreditsPayable',N'238095',N'Saldos a favor de clientes',N'Liability',1),
      (N'OutputVat',N'240805',N'IVA generado',N'Liability',0),
      (N'InputVat',N'240810',N'IVA descontable',N'Asset',0),
      (N'WithholdingIncomeTaxPayable',N'236540',N'Retención en la fuente por pagar',N'Liability',0),
      (N'WithholdingVatPayable',N'236701',N'Retención de IVA por pagar',N'Liability',0),
      (N'WithholdingIcaPayable',N'236805',N'Retención de ICA por pagar',N'Liability',0),
      (N'OwnerContributions',N'311505',N'Aportes sociales',N'Equity',0),
      (N'SalesRevenue',N'413595',N'Ingresos por ventas',N'Revenue',0),
      (N'SalesReturns',N'417595',N'Devoluciones en ventas',N'ContraRevenue',0),
      (N'OtherIncome',N'429595',N'Otros ingresos',N'Revenue',0),
      (N'OperatingExpense',N'519510',N'Gastos operativos',N'Expense',0),
      (N'PurchasesExpense',N'519595',N'Compras no inventariables',N'Expense',0),
      (N'OtherExpense',N'539595',N'Otros gastos',N'Expense',0),
      (N'CostOfGoodsSold',N'613595',N'Costo de ventas',N'Expense',0);

    INSERT dbo.AccountingAccounts(
        AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
    SELECT NEWID(),@TenantId,a.Code,a.Name,a.AccountType,1,a.RequiresParty,1,@Now
    FROM @Accounts a
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AccountingAccounts currentAccount
                      WHERE currentAccount.TenantId=@TenantId AND currentAccount.Code=a.Code);

    DECLARE @Year int=DATEPART(year,SWITCHOFFSET(@Now,'-05:00'));
    DECLARE @StartsOn date=DATEFROMPARTS(@Year,1,1),@EndsOn date=DATEFROMPARTS(@Year,12,31);
    IF NOT EXISTS (SELECT 1 FROM dbo.AccountingPeriods
                   WHERE TenantId=@TenantId AND StartsOn=@StartsOn AND EndsOn=@EndsOn)
        INSERT dbo.AccountingPeriods(PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
        VALUES(NEWID(),@TenantId,CONVERT(nvarchar(4),@Year),@StartsOn,@EndsOn,N'Open',@Now);

    INSERT dbo.AccountingAccountMappings(
        MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
    SELECT NEWID(),@TenantId,NULL,a.Category,account.AccountId,@StartsOn,NULL,@Now
    FROM @Accounts a
    INNER JOIN dbo.AccountingAccounts account ON account.TenantId=@TenantId AND account.Code=a.Code
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AccountingAccountMappings mapping
                      WHERE mapping.TenantId=@TenantId AND mapping.BusinessId IS NULL
                        AND mapping.Category=a.Category AND mapping.EffectiveFrom=@StartsOn);

    IF NOT EXISTS (SELECT 1 FROM dbo.AccountingCostCenters WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1)
        INSERT dbo.AccountingCostCenters(
            CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
        VALUES(NEWID(),@BusinessId,N'PRINCIPAL',N'Operación principal',NULL,1,1,@Now);

    IF NOT EXISTS (SELECT 1 FROM dbo.AccountingVoucherCursors WHERE TenantId=@TenantId)
        INSERT dbo.AccountingVoucherCursors(TenantId,LastAssignedNumber,UpdatedAt)
        VALUES(@TenantId,0,@Now);
END;
