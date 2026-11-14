-- Required-category coverage is evaluated after selecting the winning mapping.
-- An invalid site override cannot be hidden by a valid shared mapping.
CREATE FUNCTION accounting.RequiredPostingAccountReadiness(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@OccurredOn date)
RETURNS TABLE
AS RETURN
(
    SELECT definition.Category,definition.DisplayName,
      configured.AccountId ConfiguredAccountId,eligible.AccountId ReadyAccountId
    FROM dbo.AccountingConfigurationProfiles profile
    JOIN dbo.AccountingConfigurationProfileAccounts definition
      ON definition.ProfileCode=profile.ProfileCode AND definition.IsRequired=1
    JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
      AND business.TenantId=@TenantId AND business.IsActive=1
    OUTER APPLY accounting.ConfiguredPostingAccountIdentity(@TenantId,@BusinessId,definition.Category,@OccurredOn) configured
    OUTER APPLY accounting.PostingAccountEligibility(@TenantId,definition.Category,configured.AccountId) eligible
    WHERE profile.IsDefault=1 AND profile.IsActive=1
);
GO
