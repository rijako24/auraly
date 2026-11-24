-- Delivery financing now uses native Credit forms and the receivable ledger.
-- Retire capture only: issued documents, bindings and ledger balances survive.
:r .\20261123_PreflightRetirePendingCollection.sql
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @RetiredPaymentNow datetimeoffset(7)=SYSDATETIMEOFFSET();

DECLARE @RetiredPaymentTenants TABLE(TenantId uniqueidentifier);
UPDATE form SET IsActive=0,Version=Version+1,UpdatedAt=@RetiredPaymentNow
OUTPUT inserted.TenantId INTO @RetiredPaymentTenants
FROM reference.PaymentMethods form JOIN reference.Options type ON type.OptionId=form.PaymentTypeId
WHERE type.CatalogCode=N'payment-method' AND type.Code=N'PendingCollection' AND form.IsActive=1;

INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
SELECT NEWID(),business.BusinessId,N'Configuration',COALESCE(latest.CursorValue,0)+1,@RetiredPaymentNow
FROM dbo.Businesses business JOIN(SELECT DISTINCT TenantId FROM @RetiredPaymentTenants) changed ON changed.TenantId=business.TenantId
OUTER APPLY(SELECT MAX(AvailableThroughCursor) CursorValue FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
  WHERE BusinessId=business.BusinessId AND Stream=N'Configuration') latest;
COMMIT TRANSACTION;
GO
