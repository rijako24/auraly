CREATE PROCEDURE [dbo].[PosSecuritySynchronizationEnqueue]
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER = NULL,
    @RoleId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @BusinessId UNIQUEIDENTIFIER;
    DECLARE @ChangedUserId UNIQUEIDENTIFIER;
    DECLARE @Cursor BIGINT;
    DECLARE changes CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT business.BusinessId,users.UserId
        FROM dbo.Businesses business
        CROSS JOIN dbo.AppUsers users
        WHERE business.TenantId=@TenantId AND business.IsActive=1
          AND users.TenantId=@TenantId
          AND (@UserId IS NULL OR users.UserId=@UserId)
          AND (@RoleId IS NULL OR EXISTS(
              SELECT 1 FROM dbo.UserRoles assignment
              WHERE assignment.UserId=users.UserId AND assignment.RoleId=@RoleId
                AND (assignment.BusinessId IS NULL OR assignment.BusinessId=business.BusinessId)))
          AND (@UserId IS NOT NULL OR @RoleId IS NOT NULL)
        ORDER BY business.BusinessId,users.UserId;

    OPEN changes;
    FETCH NEXT FROM changes INTO @BusinessId,@ChangedUserId;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
        FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
        WHERE BusinessId=@BusinessId AND Stream=N'Security';

        INSERT dbo.PosSynchronizationOutboxMessages
            (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt,
             EntityType,EntityId,ChangeKind)
        VALUES(NEWID(),@BusinessId,N'Security',@Cursor,SYSUTCDATETIME(),
               N'User',@ChangedUserId,N'Upsert');

        FETCH NEXT FROM changes INTO @BusinessId,@ChangedUserId;
    END
    CLOSE changes;
    DEALLOCATE changes;
END
GO
