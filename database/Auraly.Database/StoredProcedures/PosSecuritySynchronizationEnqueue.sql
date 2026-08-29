CREATE PROCEDURE [dbo].[PosSecuritySynchronizationEnqueue]
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @BusinessId UNIQUEIDENTIFIER;
    DECLARE @Cursor BIGINT;
    DECLARE businesses CURSOR LOCAL FAST_FORWARD FOR
        SELECT BusinessId
        FROM dbo.Businesses
        WHERE TenantId=@TenantId AND IsActive=1
        ORDER BY BusinessId;

    OPEN businesses;
    FETCH NEXT FROM businesses INTO @BusinessId;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
        FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
        WHERE BusinessId=@BusinessId AND Stream=N'Security';

        INSERT dbo.PosSynchronizationOutboxMessages
            (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
        VALUES(NEWID(),@BusinessId,N'Security',@Cursor,SYSUTCDATETIME());

        FETCH NEXT FROM businesses INTO @BusinessId;
    END
    CLOSE businesses;
    DEALLOCATE businesses;
END
GO
