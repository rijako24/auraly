CREATE PROCEDURE worksessions.DispatchSessionEnsure
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER,
    @ActorId UNIQUEIDENTIFIER,
    @Administrator BIT,
    @SessionId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT=0 THROW 51000,N'La sesión de recaudo requiere la transacción del despacho.',1;
    DECLARE @Driver UNIQUEIDENTIFIER,@Opened DATETIMEOFFSET(7);
    SELECT @Driver=DriverUserId,@Opened=COALESCE(ReleasedAt,CreatedAt)
    FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK)
    WHERE DispatchId=@DispatchId AND TenantId=@TenantId AND BusinessId=@BusinessId
      AND (@Administrator=1 OR DriverUserId=@ActorId)
      AND Status IN(N'Released',N'InDelivery',N'PendingSettlement');
    IF @Driver IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.AppUsers
      WHERE UserId=@Driver AND TenantId=@TenantId AND IsActive=1)
      THROW 51000,N'El despacho no tiene un transportador activo en esta empresa.',1;
    SELECT @SessionId=WorkSessionId FROM dbo.WorkSessions WITH(UPDLOCK,HOLDLOCK)
    WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND DispatchId=@DispatchId AND Kind=N'Dispatch';
    IF @SessionId IS NOT NULL
    BEGIN
      IF NOT EXISTS(SELECT 1 FROM dbo.WorkSessions WHERE WorkSessionId=@SessionId AND Status=N'Open' AND UserId=@Driver)
        THROW 51000,N'La sesión de recaudo ya cerró o pertenece a otro transportador.',1;
      RETURN;
    END;
    SET @SessionId=NEWID();
    INSERT dbo.WorkSessions(WorkSessionId,TenantId,BusinessId,UserId,Kind,DispatchId,OpenedAt,LastActivityAt,Status)
    VALUES(@SessionId,@TenantId,@BusinessId,@Driver,N'Dispatch',@DispatchId,@Opened,@Opened,N'Open');
END
