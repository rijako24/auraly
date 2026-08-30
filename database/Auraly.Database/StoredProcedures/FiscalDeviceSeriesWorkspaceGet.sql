CREATE PROCEDURE [fiscal].[FiscalDeviceSeriesWorkspaceGet]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.Businesses
        WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
        THROW 51021,N'La sede no pertenece a la empresa autenticada.',1;

    SELECT r.DianNumberingRangeId,r.AuthorizationNumber,r.Prefix,
           r.RangeStart,r.RangeEnd,r.ValidFrom,r.ValidUntil
    FROM fiscal.DianNumberingRanges r
    WHERE r.TenantId=@TenantId AND r.AssignedBusinessId IS NULL
      AND r.ValidUntil>=CONVERT(date,SYSUTCDATETIME())
    ORDER BY r.ValidUntil,r.Prefix,r.RangeStart;

    SELECT d.DeviceId,d.Name,d.IsActive,d.LastSeenAt,b.BusinessId,b.Name,
           fs.SeriesId,fs.FiscalAuthorizationId,auth.AuthorizationNumber,
           fs.Prefix,fs.RangeStart,fs.RangeEnd
    FROM dbo.EnrolledDevices d
    JOIN (
        SELECT DeviceId,BusinessId,
               ROW_NUMBER() OVER(PARTITION BY DeviceId ORDER BY IsActive DESC,CreatedAt DESC,DocumentSeriesId) Position
        FROM dbo.DocumentSeries WHERE DeviceId IS NOT NULL
    ) scope ON scope.DeviceId=d.DeviceId AND scope.Position=1
    JOIN dbo.Businesses b ON b.BusinessId=scope.BusinessId
    OUTER APPLY(
        SELECT TOP(1) s.SeriesId,s.FiscalAuthorizationId,s.Prefix,s.RangeStart,s.RangeEnd
        FROM dbo.FiscalSeries s
        WHERE s.BusinessId=@BusinessId AND s.DeviceId=d.DeviceId
          AND s.EmitterKind=N'Device' AND s.DocumentType=N'SalesInvoice'
          AND s.IsActive=1
        ORDER BY s.CreatedAt DESC,s.SeriesId
    ) fs
    LEFT JOIN dbo.FiscalAuthorizations auth
      ON auth.FiscalAuthorizationId=fs.FiscalAuthorizationId
    WHERE d.TenantId=@TenantId AND scope.BusinessId=@BusinessId
    ORDER BY d.IsActive DESC,d.Name,d.DeviceId;
END;
GO
