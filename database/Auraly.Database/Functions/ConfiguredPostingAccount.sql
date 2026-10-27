-- The winning configured override is validated once. An invalid site override
-- cannot silently fall through to a different shared account.
-- Retain the selected account lock until acceptance freezes its identity and
-- configuration. Concurrent acceptances must not race a semantic edit or each
-- other when upgrading the first-use configuration protection.
CREATE FUNCTION accounting.ConfiguredPostingAccount(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@Category nvarchar(128),@OccurredOn date)
RETURNS TABLE
AS RETURN
(
    SELECT ready.AccountId
    FROM accounting.ConfiguredPostingAccountIdentity(@TenantId,@BusinessId,@Category,@OccurredOn) mapping
    CROSS APPLY accounting.PostingAccountReadiness(@TenantId,@BusinessId,@Category,mapping.AccountId) ready
);
GO
