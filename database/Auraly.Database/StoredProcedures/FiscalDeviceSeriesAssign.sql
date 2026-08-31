CREATE PROCEDURE [fiscal].[FiscalDeviceSeriesAssign]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @DeviceId UNIQUEIDENTIFIER,
    @DianNumberingRangeId UNIQUEIDENTIFIER,
    @FiscalAuthorizationId UNIQUEIDENTIFIER,
    @FiscalTechnicalKeySecretId UNIQUEIDENTIFIER,
    @SeriesId UNIQUEIDENTIFIER,
    @NotificationId UNIQUEIDENTIFIER,
    @QrValidationUrl NVARCHAR(500),
    @TechnicalKeyVersion NVARCHAR(64),
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.EnrolledDevices d WITH (UPDLOCK,HOLDLOCK)
        JOIN dbo.DocumentSeries ds ON ds.DeviceId=d.DeviceId
        JOIN dbo.Businesses b ON b.BusinessId=ds.BusinessId
        WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
          AND ds.BusinessId=@BusinessId AND ds.IsActive=1 AND b.IsActive=1)
        THROW 51023,N'El equipo no está enrolado y activo en la sede seleccionada.',1;

    IF EXISTS (
        SELECT 1 FROM dbo.FiscalSeries s WITH (UPDLOCK,HOLDLOCK)
        JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
        WHERE s.BusinessId=@BusinessId AND s.DeviceId=@DeviceId
          AND s.DocumentType=N'SalesInvoice' AND s.IsActive=1
          AND a.DianNumberingRangeId=@DianNumberingRangeId)
    BEGIN
        RETURN;
    END;

    DECLARE @AuthorizationNumber NVARCHAR(64),@Prefix NVARCHAR(16),
            @RangeStart BIGINT,@RangeEnd BIGINT,@ValidFrom DATE,@ValidUntil DATE,
            @ProtectedTechnicalKey VARBINARY(MAX),@SupplierTaxId NVARCHAR(32),
            @PreviousAuthorizationId UNIQUEIDENTIFIER;

    SELECT @AuthorizationNumber=r.AuthorizationNumber,@Prefix=r.Prefix,
           @RangeStart=r.RangeStart,@RangeEnd=r.RangeEnd,
           @ValidFrom=r.ValidFrom,@ValidUntil=r.ValidUntil,
           @ProtectedTechnicalKey=r.ProtectedTechnicalKey
    FROM fiscal.DianNumberingRanges r WITH (UPDLOCK,HOLDLOCK)
    WHERE r.DianNumberingRangeId=@DianNumberingRangeId
      AND r.TenantId=@TenantId AND r.AssignedBusinessId IS NULL
      AND r.ValidFrom<=CONVERT(date,@Now) AND r.ValidUntil>=CONVERT(date,@Now);

    IF @AuthorizationNumber IS NULL
        THROW 51027,N'La resolución DIAN no está disponible, no está vigente o ya tiene un emisor.',1;

    SELECT TOP(1) @SupplierTaxId=configuration.SupplierTaxId
    FROM dbo.FiscalIssuerConfigurations configuration
    JOIN dbo.Businesses configuredBusiness ON configuredBusiness.BusinessId=configuration.BusinessId
    WHERE configuredBusiness.TenantId=@TenantId AND configuration.Environment IN(1,2)
      AND configuration.IsActive=1 AND configuration.ValidFrom<=@Now
      AND (configuration.ValidTo IS NULL OR configuration.ValidTo>@Now)
    ORDER BY CASE WHEN configuration.BusinessId=@BusinessId AND configuration.Environment=1 THEN 0 ELSE 1 END,
             configuration.Version DESC,configuration.CreatedAt DESC;
    IF @SupplierTaxId IS NULL
        THROW 51027,N'Completa primero la configuración de habilitación DIAN.',1;

    UPDATE fiscal.DianNumberingRanges
    SET AssignedBusinessId=@BusinessId,AssignedAt=@Now,AssignedByUserId=@UserId
    WHERE DianNumberingRangeId=@DianNumberingRangeId AND AssignedBusinessId IS NULL;
    IF @@ROWCOUNT<>1
        THROW 51027,N'La resolución DIAN fue asignada simultáneamente a otro emisor.',1;

    -- La asignación pertenece al equipo y puede reemplazarse sin depender del
    -- estado global de producción. El histórico permanece inmutable.
    SELECT TOP(1) @PreviousAuthorizationId=FiscalAuthorizationId
    FROM dbo.FiscalSeries WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
      AND DocumentType=N'SalesInvoice' AND IsActive=1;

    UPDATE dbo.FiscalSeries
    SET IsActive=0
    WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
      AND DocumentType=N'SalesInvoice' AND IsActive=1;

    UPDATE dbo.FiscalAuthorizations
    SET IsActive=0
    WHERE FiscalAuthorizationId=@PreviousAuthorizationId AND IsActive=1
      AND NOT EXISTS(
          SELECT 1 FROM dbo.FiscalSeries activeSeries
          WHERE activeSeries.FiscalAuthorizationId=@PreviousAuthorizationId
            AND activeSeries.IsActive=1);

    INSERT dbo.FiscalAuthorizations(
        FiscalAuthorizationId,DianNumberingRangeId,BusinessId,AuthorizationNumber,SupplierTaxId,Environment,
        QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,AuthorizedRangeStart,
        AuthorizedRangeEnd,IsActive,CreatedAt)
    VALUES(@FiscalAuthorizationId,@DianNumberingRangeId,@BusinessId,@AuthorizationNumber,@SupplierTaxId,1,
           @QrValidationUrl,@TechnicalKeyVersion,@ValidFrom,@ValidUntil,@RangeStart,
           @RangeEnd,1,@Now);

    INSERT dbo.FiscalTechnicalKeySecrets(
        FiscalTechnicalKeySecretId,BusinessId,FiscalAuthorizationId,TechnicalKeyVersion,
        Environment,ProtectedValue,CreatedAt,UpdatedAt)
    VALUES(@FiscalTechnicalKeySecretId,@BusinessId,@FiscalAuthorizationId,@TechnicalKeyVersion,
           1,@ProtectedTechnicalKey,@Now,@Now);

    INSERT dbo.FiscalSeries(
        SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
        DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
    VALUES(@SeriesId,@BusinessId,@DeviceId,N'Device',@FiscalAuthorizationId,
           N'SalesInvoice',@Prefix,@RangeStart,@RangeEnd,1,@Now);

    DECLARE @Cursor BIGINT;
    SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
    FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning';
    INSERT dbo.PosSynchronizationOutboxMessages(
        NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
    VALUES(@NotificationId,@BusinessId,N'FiscalProvisioning',@Cursor,@Now);
END;
GO
