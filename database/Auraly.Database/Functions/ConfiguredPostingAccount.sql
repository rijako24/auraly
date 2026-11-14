-- The winning configured override is validated once. An invalid site override
-- cannot silently fall through to a different shared account.
-- The posting transaction retains the selected mapping/account locks through
-- journal completion. Acceptance does not resolve automatic configuration.
CREATE FUNCTION accounting.ConfiguredPostingAccount(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@Category nvarchar(128),@OccurredOn date)
RETURNS TABLE
AS RETURN
(
    SELECT ready.AccountId
    FROM accounting.ConfiguredPostingAccountIdentity(@TenantId,@BusinessId,@Category,@OccurredOn) mapping
    CROSS APPLY accounting.PostingAccountReadiness(@TenantId,@Category,mapping.AccountId) ready
);
GO
