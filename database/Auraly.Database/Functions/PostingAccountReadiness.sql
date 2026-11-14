-- One readiness policy for the automatic configuration selected at posting.
-- Only accounts actually requested for posting take the first-use update lock.
CREATE FUNCTION accounting.PostingAccountReadiness(
    @TenantId uniqueidentifier,@Category nvarchar(128),@AccountId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    -- Lock the clustered identity row. A covering secondary-index key need not
    -- block an edit of semantic columns absent from that index.
    SELECT account.AccountId FROM dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK,INDEX(PK_AccountingAccounts))
    JOIN accounting.PostingAccountEligibility(@TenantId,@Category,@AccountId) eligible
      ON eligible.AccountId=account.AccountId
    WHERE account.AccountId=@AccountId AND account.TenantId=@TenantId
);
GO
