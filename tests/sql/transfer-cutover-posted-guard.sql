-- Run after transfer-cutover-regression.sql on the same disposable database.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @closureId uniqueidentifier,@tenantId uniqueidentifier,
  @businessId uniqueidentifier,@snapshot nvarchar(max),@now datetimeoffset(7);
SELECT TOP(1) @closureId=closure.WorkSessionClosureId,
  @tenantId=session.TenantId,@businessId=session.BusinessId,
  @snapshot=closure.ReceiptSnapshotJson,@now=closure.ClosedAt
FROM dbo.WorkSessionClosures closure
JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
JOIN dbo.AuditLogs audit ON audit.EntityType=N'WorkSessionClosure'
  AND audit.EntityId=CONVERT(nvarchar(36),closure.WorkSessionClosureId)
  AND audit.Action=N'NormalizeTransferPaymentMethod'
ORDER BY closure.ClosedAt DESC;
IF @closureId IS NULL THROW 52010,N'Primero ejecute la prueba de migración.',1;

DECLARE @totals nvarchar(max)=N'[{"paymentMethodCode":"BankTransfer","salesAmount":0,"refundAmount":0,"otherAmount":697114.60,"netAmount":697114.60,"countedAmount":null,"difference":null,"requiresCount":false,"cashEntryAmount":0,"cashExitAmount":0,"receivableAmount":697114.60,"payableAmount":0},{"paymentMethodCode":"Transfer","salesAmount":0,"refundAmount":0,"otherAmount":0,"netAmount":0,"countedAmount":697925,"difference":697925,"requiresCount":true,"cashEntryAmount":0,"cashExitAmount":0,"receivableAmount":0,"payableAmount":0}]';
SET @snapshot=JSON_MODIFY(@snapshot,N'$.paymentTotals',JSON_QUERY(@totals));
UPDATE dbo.WorkSessionClosures
SET ReceiptSnapshotJson=@snapshot,
  ReceiptHash=HASHBYTES('SHA2_256',CONVERT(varbinary(max),
    CONVERT(varchar(max),@snapshot COLLATE Latin1_General_100_CI_AS_SC_UTF8)))
WHERE WorkSessionClosureId=@closureId;
UPDATE dbo.WorkSessionClosurePaymentTotals
SET SalesAmount=0,RefundAmount=0,OtherAmount=0,NetAmount=0,
  CountedAmount=697925,Difference=697925
WHERE WorkSessionClosureId=@closureId AND PaymentMethodCode=N'Transfer';
INSERT dbo.WorkSessionClosurePaymentTotals(WorkSessionClosureId,PaymentMethodCode,
  SalesAmount,RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference)
VALUES(@closureId,N'BankTransfer',0,0,697114.60,697114.60,NULL,NULL);
INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,
  SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,
  Status,AttemptCount,CreatedAt)
VALUES(NEWID(),@tenantId,@businessId,@closureId,N'WorkSessionCashDifference',
  HASHBYTES('SHA2_256',CONVERT(varbinary(max),N'guard-test')),
  @now,N'Posted',0,@now);

BEGIN TRY
:r database/Auraly.Database/Scripts/Migrations/20260925_NormalizeCommerceTransferCode.sql
  THROW 52011,N'Se permitió cambiar un cierre contabilizado sin corrección.',1;
END TRY
BEGIN CATCH
  DECLARE @error int=ERROR_NUMBER();
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  IF @error<>51004 THROW;
END CATCH;
IF NOT EXISTS(SELECT 1 FROM dbo.WorkSessionClosurePaymentTotals
  WHERE WorkSessionClosureId=@closureId AND PaymentMethodCode=N'BankTransfer'
    AND NetAmount=697114.60)
  THROW 52012,N'La migración modificó datos antes de verificar el comprobante.',1;
PRINT 'Posted-closure correction guard passed.';
