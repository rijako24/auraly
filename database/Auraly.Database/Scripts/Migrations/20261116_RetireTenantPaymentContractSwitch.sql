-- Retire the tenant feature switch before DACPAC planning. The protocol version
-- remains part of the snapshot; every current capture uses the tenant catalog.
-- Old consumers must be stopped by the coordinated deployment first.
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;
  IF COL_LENGTH(N'dbo.Tenants',N'PaymentMethodContractVersion') IS NOT NULL
  BEGIN
    ALTER TABLE dbo.Tenants DROP CONSTRAINT CK_Tenants_PaymentMethodContractVersion;
    ALTER TABLE dbo.Tenants DROP CONSTRAINT DF_Tenants_PaymentMethodContractVersion;
    ALTER TABLE dbo.Tenants DROP COLUMN PaymentMethodContractVersion;
    -- Existing prepared boxes can already have all four forms, so the catalog
    -- bootstrap may insert nothing. Advance the existing Configuration stream
    -- once at this cutover, or its equal-cursor check retains protocol 1.
    INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
    SELECT NEWID(),business.BusinessId,N'Configuration',COALESCE(latest.CursorValue,0)+1,SYSDATETIMEOFFSET()
    FROM dbo.Businesses business
    OUTER APPLY(
      SELECT MAX(AvailableThroughCursor) CursorValue
      FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
      WHERE BusinessId=business.BusinessId AND Stream=N'Configuration') latest;
  END;
  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
GO
