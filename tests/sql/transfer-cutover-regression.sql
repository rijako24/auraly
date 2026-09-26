-- Run against a freshly deployed disposable SQL Server 2019+ database.
-- sqlcmd -S .\TEST -E -d <scratch-db> -b -i tests\sql\transfer-cutover-regression.sql
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
DECLARE @businessId uniqueidentifier,@tenantId uniqueidentifier,@userId uniqueidentifier;
SELECT TOP(1) @businessId=business.BusinessId,@tenantId=business.TenantId,
  @userId=person.UserId
FROM dbo.Businesses business
JOIN dbo.AppUsers person ON person.TenantId=business.TenantId
ORDER BY business.BusinessId;
IF @userId IS NULL THROW 52001,N'El test necesita una empresa y un usuario.',1;

DECLARE @sessionId uniqueidentifier=NEWID(),@closureId uniqueidentifier=NEWID();
DECLARE @now datetimeoffset(7)=SYSDATETIMEOFFSET();
DECLARE @totals nvarchar(max)=N'[{"paymentMethodCode":"BankTransfer","salesAmount":0,"refundAmount":0,"otherAmount":697114.60,"netAmount":697114.60,"countedAmount":null,"difference":null,"requiresCount":false,"cashEntryAmount":0,"cashExitAmount":0,"receivableAmount":697114.60,"payableAmount":0},{"paymentMethodCode":"Transfer","salesAmount":0,"refundAmount":0,"otherAmount":0,"netAmount":0,"countedAmount":697925,"difference":697925,"requiresCount":true,"cashEntryAmount":0,"cashExitAmount":0,"receivableAmount":0,"payableAmount":0}]';
DECLARE @snapshot nvarchar(max)=CONCAT(N'{"workSessionClosureId":"',CONVERT(nvarchar(36),@closureId),
  N'","businessName":"Prueba á","paymentTotals":',@totals,N'}');
DECLARE @hash varbinary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),
  CONVERT(varchar(max),@snapshot COLLATE Latin1_General_100_CI_AS_SC_UTF8)));

INSERT dbo.WorkSessions(WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,
  DeviceId,OpenedAt,LastActivityAt,ClosedAt,Status)
VALUES(@sessionId,@tenantId,@businessId,NULL,@userId,NULL,
  DATEADD(hour,-1,@now),@now,@now,N'Closed');
INSERT dbo.WorkSessionClosures(WorkSessionClosureId,WorkSessionId,ClosedByUserId,
  IdempotencyKey,TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,
  CountedCash,CashDifference,ReceiptSnapshotJson,ReceiptHash,ClosedAt)
VALUES(@closureId,@sessionId,@userId,CONVERT(nvarchar(36),@closureId),
  0,0,697114.60,697114.60,0,0,0,@snapshot,@hash,@now);
INSERT dbo.WorkSessionClosurePaymentTotals(WorkSessionClosureId,PaymentMethodCode,
  SalesAmount,RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference)
VALUES(@closureId,N'BankTransfer',0,0,697114.60,697114.60,NULL,NULL),
      (@closureId,N'Transfer',0,0,0,0,697925,697925);
INSERT dbo.WorkSessionMovements(WorkSessionMovementId,WorkSessionId,DocumentId,
  PaymentNumber,BusinessDate,MovementType,PaymentMethodCode,Amount,SourceKey,
  OccurredAt,RecordedByUserId)
VALUES(NEWID(),@sessionId,NULL,NULL,CONVERT(date,@now),N'ReceivablePayment',
  N'BankTransfer',697114.60,CONCAT(N'test:',CONVERT(nvarchar(36),@closureId)),@now,@userId);
INSERT worksessions.CashClosurePaymentMethodMappings(PaymentMethodCode,
  ClosureMethodCode,RequiresCount,SortOrder)
VALUES(N'BankTransfer',N'Transfer',1,30);
INSERT dbo.AccountingConfigurationProfiles(ProfileCode,Name,IsDefault,IsActive)
VALUES(N'TEST_TRANSFER_CUTOVER',N'Prueba de corte de transferencias',0,1);
INSERT dbo.AccountingConfigurationProfileAccounts(ProfileCode,Category,DisplayName,
  AccountCode,AccountName,AccountType,DisplayOrder)
VALUES(N'TEST_TRANSFER_CUTOVER',N'Bank',N'Bancos',N'111005',N'Banco de prueba',N'Asset',1);
INSERT dbo.AccountingSourceCategoryMappings(ProfileCode,SourceType,SourceCode,Category)
VALUES(N'TEST_TRANSFER_CUTOVER',N'CustomerPaymentMethod',N'BankTransfer',N'Bank');

:r database/Auraly.Database/Scripts/Migrations/20260925_NormalizeCommerceTransferCode.sql

IF EXISTS(SELECT 1 FROM dbo.WorkSessionClosurePaymentTotals
  WHERE WorkSessionClosureId=@closureId AND PaymentMethodCode=N'BankTransfer')
  THROW 52002,N'El código anterior quedó en el cierre.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.WorkSessionClosurePaymentTotals
  WHERE WorkSessionClosureId=@closureId AND PaymentMethodCode=N'Transfer'
    AND NetAmount=697114.60 AND CountedAmount=697925 AND Difference=810.40)
  THROW 52003,N'La línea unificada del cierre no cuadra.',1;
IF EXISTS(SELECT 1 FROM dbo.WorkSessionMovements
  WHERE WorkSessionId=@sessionId AND PaymentMethodCode=N'BankTransfer')
  THROW 52004,N'El movimiento aún tiene el código anterior.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.WorkSessionClosures
  WHERE WorkSessionClosureId=@closureId
    AND ReceiptHash=HASHBYTES('SHA2_256',CONVERT(varbinary(max),
      CONVERT(varchar(max),ReceiptSnapshotJson COLLATE Latin1_General_100_CI_AS_SC_UTF8)))
    AND NOT EXISTS(SELECT 1 FROM OPENJSON(ReceiptSnapshotJson,N'$.paymentTotals')
      WITH(paymentMethodCode nvarchar(32)) total
      WHERE total.paymentMethodCode=N'BankTransfer'))
  THROW 52005,N'La huella o el contenido de la tirilla es incorrecto.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE EntityType=N'WorkSessionClosure'
  AND EntityId=CONVERT(nvarchar(36),@closureId)
  AND Action=N'NormalizeTransferPaymentMethod'
  AND JSON_VALUE(OldValues,N'$.receiptSnapshotJson') IS NOT NULL
  AND JSON_VALUE(NewValues,N'$.receiptSnapshotJson') IS NOT NULL)
  THROW 52006,N'No quedó auditoría del cierre anterior.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE EntityType=N'WorkSessionMovement'
  AND Action=N'NormalizeTransferPaymentMethod'
  AND JSON_VALUE(OldValues,N'$.paymentMethodCode')=N'BankTransfer'
  AND JSON_VALUE(NewValues,N'$.paymentMethodCode')=N'Transfer')
  THROW 52009,N'No quedó auditoría del movimiento anterior.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceCategoryMappings
  WHERE ProfileCode=N'TEST_TRANSFER_CUTOVER'
    AND SourceType=N'CustomerPaymentMethod' AND SourceCode=N'Transfer'
    AND Category=N'Bank')
  THROW 52013,N'No se conservó la configuración contable de otro perfil.',1;
PRINT 'Transfer cutover regression passed.';
