-- A purpose can require the tenant to choose an actual auxiliary without
-- suggesting a fabricated default account. All bootstrap surfaces reuse this
-- projection; the profile table remains the sole catalog and label owner.
CREATE FUNCTION accounting.DefaultAccountDefinitions(@ProfileCode nvarchar(32))
RETURNS TABLE
AS RETURN
  SELECT ProfileCode,Category,AccountCode,AccountName,AccountType,
    AllowsPosting,RequiresParty,IsRequired,DisplayOrder
  FROM dbo.AccountingConfigurationProfileAccounts
  WHERE ProfileCode=@ProfileCode AND AccountCode IS NOT NULL;
GO
