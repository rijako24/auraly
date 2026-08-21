DECLARE @AccountingSeedTenantId uniqueidentifier,
        @AccountingSeedBusinessId uniqueidentifier,
        @AccountingSeedTimestamp datetimeoffset(7)=SYSUTCDATETIME();
DECLARE accounting_defaults_cursor CURSOR LOCAL FAST_FORWARD FOR
  SELECT TenantId,BusinessId FROM dbo.Businesses WHERE IsActive=1;
OPEN accounting_defaults_cursor;
FETCH NEXT FROM accounting_defaults_cursor INTO @AccountingSeedTenantId,@AccountingSeedBusinessId;
WHILE @@FETCH_STATUS=0
BEGIN
  EXEC dbo.AccountingDefaultsProvision
    @TenantId=@AccountingSeedTenantId,
    @BusinessId=@AccountingSeedBusinessId,
    @Now=@AccountingSeedTimestamp;
  FETCH NEXT FROM accounting_defaults_cursor INTO @AccountingSeedTenantId,@AccountingSeedBusinessId;
END
CLOSE accounting_defaults_cursor;
DEALLOCATE accounting_defaults_cursor;
PRINT 'Configuración contable y conceptos de gasto iniciales garantizados.';
