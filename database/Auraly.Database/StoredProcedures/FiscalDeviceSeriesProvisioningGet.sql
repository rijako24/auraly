CREATE PROCEDURE [fiscal].[FiscalDeviceSeriesProvisioningGet]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @DeviceId UNIQUEIDENTIFIER,
    @CurrentSeriesId UNIQUEIDENTIFIER = NULL,
    @NextConsecutive BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.EnrolledDevices d
        JOIN dbo.DocumentSeries ds ON ds.DeviceId=d.DeviceId
        WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
          AND ds.BusinessId=@BusinessId AND ds.IsActive=1)
        THROW 51023,N'El equipo no está enrolado y activo en la sede seleccionada.',1;

    IF @CurrentSeriesId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM dbo.FiscalSeries
        WHERE SeriesId=@CurrentSeriesId AND BusinessId=@BusinessId
          AND DeviceId=@DeviceId AND EmitterKind=N'Device')
        THROW 51027,N'El cursor fiscal informado no pertenece al equipo.',1;

    SELECT s.SeriesId,s.FiscalAuthorizationId,s.Prefix,a.AuthorizationNumber,
           s.RangeStart,s.RangeEnd,a.ValidFrom,a.ValidUntil,a.Environment,
           a.SupplierTaxId,a.TechnicalKeyVersion,a.QrValidationUrl,
           a.AuthorizedRangeStart,a.AuthorizedRangeEnd
    FROM dbo.FiscalSeries s
    JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
    WHERE s.BusinessId=@BusinessId AND s.DeviceId=@DeviceId
      AND s.EmitterKind=N'Device' AND s.DocumentType=N'SalesInvoice'
      AND s.IsActive=1 AND a.IsActive=1;
END;
GO
