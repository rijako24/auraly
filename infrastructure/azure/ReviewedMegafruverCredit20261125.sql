-- Reviewed deployment configuration for the user's requested native customer-credit form.
-- @Apply=0 rehearses and rolls back; @Apply=1 commits. No financial documents are written.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @Tenant uniqueidentifier='01a06318-d4bf-7f57-9776-4695b7833f7a',
 @Account uniqueidentifier='4faabe66-23b5-42de-9004-3cf3fab82b25',
 @Form uniqueidentifier='701aa552-6b6f-4289-80fb-4ea57a788ff1',
 @Now datetimeoffset(7)=SYSUTCDATETIME();
IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts a WITH(UPDLOCK,HOLDLOCK)
 JOIN dbo.AccountingAccountMappings m ON m.TenantId=a.TenantId AND m.AccountId=a.AccountId
 WHERE a.TenantId=@Tenant AND a.AccountId=@Account AND a.Code=N'130505'
 AND a.Name=N'Clientes nacionales' AND a.AccountType=N'Asset' AND a.IsActive=1 AND a.AllowsPosting=1
 AND a.RequiresParty=1 AND a.BankAccountId IS NULL AND a.ParentAccountId IS NULL AND a.LegacyBusinessId IS NULL
 AND (a.FinancialFunction IS NULL OR a.FinancialFunction=N'Receivable')
 AND (a.BalanceSide IS NULL OR a.BalanceSide=N'Debit')
 AND m.Category=N'AccountsReceivable' AND m.EffectiveFrom<='20261125' AND (m.EffectiveTo IS NULL OR m.EffectiveTo>='20261125'))
 THROW 51991,N'La cuenta de cartera no coincide con la identidad y configuración verificadas.',1;
IF NOT EXISTS(SELECT 1 FROM reference.PaymentMethods m WITH(UPDLOCK,HOLDLOCK)
 JOIN reference.Options t ON t.OptionId=m.PaymentTypeId AND t.CatalogCode=N'payment-method' AND t.Code=N'Credit'
 WHERE m.TenantId=@Tenant AND m.PaymentMethodId=@Form AND m.Code=N'Credit' AND m.Name=N'Crédito cliente'
 AND m.AccountId=@Account AND m.UseForSales=1 AND m.UseForReceipts=0 AND m.UseForSupplierPayments=0
 AND m.UseForPayroll=0 AND m.CreatedAt>='2026-11-25T19:07:38Z')
 THROW 51992,N'La forma de crédito no corresponde al alta nativa realizada por este despliegue.',1;
UPDATE dbo.AccountingAccounts SET FinancialFunction=N'Receivable',BalanceSide=N'Debit'
 WHERE TenantId=@Tenant AND AccountId=@Account AND (FinancialFunction IS NULL OR BalanceSide IS NULL);
IF NOT EXISTS(SELECT 1 FROM accounting.PaymentMethodAccountReadiness(@Tenant,N'Credit',@Account)
 WHERE SourceType=N'PosPaymentMethod')
 THROW 51993,N'La cuenta de crédito no satisface la política canónica de contabilización.',1;
DECLARE @Changed bit=0;
UPDATE reference.PaymentMethods SET IsActive=1,Version=Version+1,UpdatedAt=@Now
 WHERE TenantId=@Tenant AND PaymentMethodId=@Form AND IsActive=0;
IF @@ROWCOUNT>0 SET @Changed=1;
IF NOT EXISTS(SELECT 1 FROM reference.PaymentMethodDetails WHERE TenantId=@Tenant AND PaymentMethodId=@Form
 AND IsActive=1 AND IsReady=1) THROW 51994,N'La forma de crédito no quedó disponible.',1;
IF @Changed=1
 INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
 SELECT NEWID(),b.BusinessId,N'Configuration',COALESCE(latest.CursorValue,0)+1,@Now
 FROM dbo.Businesses b OUTER APPLY(SELECT MAX(AvailableThroughCursor) CursorValue
 FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
 WHERE BusinessId=b.BusinessId AND Stream=N'Configuration') latest WHERE b.TenantId=@Tenant AND b.IsActive=1;
SELECT @Apply Applied,@Changed FormActivated,
 (SELECT COUNT_BIG(*) FROM dbo.Receivables WHERE BusinessId='01a06318-d4fe-7bfd-b23a-41a17f2fdd88') Receivables,
 (SELECT COUNT_BIG(*) FROM dbo.AccountingEntries WHERE BusinessId='01a06318-d4fe-7bfd-b23a-41a17f2fdd88') AccountingEntries
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
IF @Apply=1 COMMIT TRANSACTION; ELSE ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
