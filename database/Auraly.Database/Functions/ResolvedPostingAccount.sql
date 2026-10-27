-- A source with a snapshot never falls back to today's mapping. A NULL snapshot
-- is retained only for legacy/excluded sources, before their canonical freeze.
CREATE FUNCTION accounting.ResolvedPostingAccount(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@Category nvarchar(128),
    @OccurredOn date,@ResolvedMappings nvarchar(max))
RETURNS TABLE
AS RETURN
(
    SELECT ready.AccountId
    FROM OPENJSON(@ResolvedMappings) WITH(Category nvarchar(128),AccountId uniqueidentifier) frozen
    CROSS APPLY accounting.PostingAccountReadiness(@TenantId,@BusinessId,@Category,frozen.AccountId) ready
    WHERE @ResolvedMappings IS NOT NULL AND frozen.Category=@Category
    UNION ALL
    SELECT currentValue.AccountId
    FROM accounting.ConfiguredPostingAccount(@TenantId,@BusinessId,@Category,@OccurredOn) currentValue
    WHERE @ResolvedMappings IS NULL
);
GO
