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
  END;
  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
GO
