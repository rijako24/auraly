-- Shared acceptance guard. The caller's transaction keeps the session open
-- until its immutable money intent has been accepted.
CREATE FUNCTION worksessions.OpenSessionForUser
(@TenantId uniqueidentifier,@BusinessId uniqueidentifier,@UserId uniqueidentifier,@SessionId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    SELECT WorkSessionId,DeviceId FROM dbo.WorkSessions WITH(UPDLOCK,HOLDLOCK)
    WHERE WorkSessionId=@SessionId AND TenantId=@TenantId AND BusinessId=@BusinessId
      AND UserId=@UserId AND Status=N'Open'
);
GO
