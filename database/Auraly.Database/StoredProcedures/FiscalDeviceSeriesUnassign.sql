CREATE PROCEDURE [fiscal].[FiscalDeviceSeriesUnassign]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @DeviceId UNIQUEIDENTIFIER,
    @NotificationId UNIQUEIDENTIFIER,
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.EnrolledDevices device WITH (UPDLOCK,HOLDLOCK)
        JOIN dbo.DocumentSeries documentSeries ON documentSeries.DeviceId=device.DeviceId
        WHERE device.DeviceId=@DeviceId AND device.TenantId=@TenantId
          AND documentSeries.BusinessId=@BusinessId AND documentSeries.IsActive=1)
        THROW 51023,N'El equipo no pertenece a la sede seleccionada.',1;

    DECLARE @AuthorizationId UNIQUEIDENTIFIER;
    SELECT TOP(1) @AuthorizationId=FiscalAuthorizationId
    FROM dbo.FiscalSeries WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
      AND EmitterKind=N'Device' AND DocumentType=N'SalesInvoice' AND IsActive=1
    ORDER BY CreatedAt DESC,SeriesId;

    IF @AuthorizationId IS NULL RETURN;

    UPDATE dbo.FiscalSeries
    SET IsActive=0
    WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
      AND EmitterKind=N'Device' AND DocumentType=N'SalesInvoice' AND IsActive=1;

    UPDATE dbo.FiscalAuthorizations
    SET IsActive=0
    WHERE FiscalAuthorizationId=@AuthorizationId AND IsActive=1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.FiscalSeries activeSeries
          WHERE activeSeries.FiscalAuthorizationId=@AuthorizationId AND activeSeries.IsActive=1);

    -- La numeración emitida o reservada no vuelve al pool: liberarla permitiría
    -- reutilizar consecutivos en otro emisor. Se desasigna del equipo, no del historial DIAN.
    DECLARE @Cursor BIGINT;
    SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
    FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning';
    INSERT dbo.PosSynchronizationOutboxMessages(
        NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt,TargetDeviceId)
    VALUES(@NotificationId,@BusinessId,N'FiscalProvisioning',@Cursor,@Now,@DeviceId);
END;
GO
