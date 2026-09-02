CREATE PROCEDURE dbo.AccountingDefaultsProvision
    @TenantId uniqueidentifier,
    @BusinessId uniqueidentifier,
    @Now datetimeoffset(7)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
        THROW 51041,'La sede no pertenece al tenant indicado.',1;

    DECLARE @ProfileCode nvarchar(32)=(
      SELECT TOP(1) ProfileCode FROM dbo.AccountingConfigurationProfiles
      WHERE IsDefault=1 AND IsActive=1 ORDER BY ProfileCode);
    IF @ProfileCode IS NULL
        THROW 51042,'No existe un perfil contable predeterminado activo.',1;

    IF NOT EXISTS (SELECT 1 FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId)
        INSERT dbo.AccountingTenantSettings(
            TenantId,Status,FunctionalCurrencyCode,UpdatedAt)
        VALUES(@TenantId,N'Configuring',N'COP',@Now);

    INSERT dbo.AccountingAccounts(
        AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
    SELECT NEWID(),@TenantId,a.AccountCode,a.AccountName,a.AccountType,a.AllowsPosting,a.RequiresParty,1,@Now
    FROM dbo.AccountingConfigurationProfileAccounts a
    WHERE a.ProfileCode=@ProfileCode AND
      NOT EXISTS (SELECT 1 FROM dbo.AccountingAccounts currentAccount
                  WHERE currentAccount.TenantId=@TenantId AND currentAccount.Code=a.AccountCode);

    DECLARE @Year int=DATEPART(year,SWITCHOFFSET(@Now,'-05:00'));
    DECLARE @StartsOn date=DATEFROMPARTS(@Year,1,1),@EndsOn date=DATEFROMPARTS(@Year,12,31);
    IF NOT EXISTS (SELECT 1 FROM dbo.AccountingPeriods
                   WHERE TenantId=@TenantId AND StartsOn=@StartsOn AND EndsOn=@EndsOn)
        INSERT dbo.AccountingPeriods(PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
        VALUES(NEWID(),@TenantId,CONVERT(nvarchar(4),@Year),@StartsOn,@EndsOn,N'Open',@Now);

    INSERT dbo.AccountingAccountMappings(
        MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
    SELECT NEWID(),@TenantId,NULL,a.Category,account.AccountId,@StartsOn,NULL,@Now
    FROM dbo.AccountingConfigurationProfileAccounts a
    INNER JOIN dbo.AccountingAccounts account ON account.TenantId=@TenantId AND account.Code=a.AccountCode
    WHERE a.ProfileCode=@ProfileCode AND
      NOT EXISTS (SELECT 1 FROM dbo.AccountingAccountMappings mapping
                      WHERE mapping.TenantId=@TenantId AND mapping.BusinessId IS NULL
                        AND mapping.Category=a.Category AND mapping.EffectiveFrom=@StartsOn);

    IF NOT EXISTS (SELECT 1 FROM dbo.AccountingCostCenters WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1)
        INSERT dbo.AccountingCostCenters(
            CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
        VALUES(NEWID(),@BusinessId,N'PRINCIPAL',N'Operación principal',NULL,1,1,@Now);

    DECLARE @DefaultCostCenterId uniqueidentifier=(SELECT TOP(1) CostCenterId FROM dbo.AccountingCostCenters WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1 ORDER BY CreatedAt);
    INSERT dbo.ExpenseConcepts(ExpenseConceptId,BusinessId,Code,Name,ExpenseAccountId,DefaultCostCenterId,WithholdingConceptCode,IsActive,CreatedAt,UpdatedAt)
    SELECT NEWID(),@BusinessId,concept.Code,concept.Name,account.AccountId,@DefaultCostCenterId,NULL,1,@Now,@Now
    FROM dbo.AccountingConfigurationProfileExpenseConcepts concept
    INNER JOIN dbo.AccountingConfigurationProfileAccounts definition
      ON definition.ProfileCode=concept.ProfileCode AND definition.Category=concept.ExpenseAccountCategory
    INNER JOIN dbo.AccountingAccounts account
      ON account.TenantId=@TenantId AND account.Code=definition.AccountCode
    WHERE concept.ProfileCode=@ProfileCode AND concept.IsActive=1 AND NOT EXISTS(
      SELECT 1 FROM dbo.ExpenseConcepts currentConcept WHERE currentConcept.BusinessId=@BusinessId AND currentConcept.Code=concept.Code);

    IF NOT EXISTS(SELECT 1 FROM dbo.Suppliers WHERE BusinessId=@BusinessId AND Identification=N'OCASIONAL')
      INSERT dbo.Suppliers(SupplierId,BusinessId,PartyId,Identification,Name,IsActive,CreatedAt)
      VALUES(NEWID(),@BusinessId,NULL,N'OCASIONAL',N'Gasto ocasional / sin proveedor',1,@Now);

    INSERT dbo.BusinessReasons(
      ReasonId,BusinessId,ReasonType,Code,Name,Direction,CounterpartAccountingCategory,
      DefaultCostCenterId,RequiresReference,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
    SELECT NEWID(),@BusinessId,template.ReasonType,template.Code,template.Name,template.Direction,
      template.CounterpartAccountingCategory,NULL,template.RequiresReference,1,1,
      template.DisplayOrder,@Now,@Now
    FROM dbo.ReasonTemplates template
    WHERE template.ProfileCode=@ProfileCode AND template.IsActive=1 AND NOT EXISTS(
      SELECT 1 FROM dbo.BusinessReasons reason
      WHERE reason.BusinessId=@BusinessId AND reason.ReasonType=template.ReasonType AND reason.Code=template.Code);

    IF NOT EXISTS (SELECT 1 FROM dbo.AccountingVoucherCursors WHERE TenantId=@TenantId)
        INSERT dbo.AccountingVoucherCursors(TenantId,LastAssignedNumber,UpdatedAt)
        VALUES(@TenantId,0,@Now);
END;
