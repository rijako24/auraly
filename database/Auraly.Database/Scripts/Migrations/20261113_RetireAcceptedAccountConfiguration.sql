-- Coordinated application/schema cutover: stop old accounting consumers first.
-- Pending jobs and all historical evidence remain untouched. No automatic retry.
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;
  DROP PROCEDURE IF EXISTS accounting.PortfolioPaymentAccountsFreeze;
  DROP PROCEDURE IF EXISTS accounting.AccountMappingsFreeze;
  DROP PROCEDURE IF EXISTS accounting.BankAccountsFreeze;
  DROP FUNCTION IF EXISTS accounting.ResolvedPostingAccount;
  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
GO
