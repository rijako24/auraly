CREATE PROCEDURE [fiscal].[FiscalDeviceNumberingEnsure]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @DeviceId UNIQUEIDENTIFIER,
    @CurrentSeriesId UNIQUEIDENTIFIER = NULL,
    @NextConsecutive BIGINT = NULL,
    @ActiveSeriesId UNIQUEIDENTIFIER,
    @StandbySeriesId UNIQUEIDENTIFIER,
    @ActiveNotificationId UNIQUEIDENTIFIER,
    @StandbyNotificationId UNIQUEIDENTIFIER,
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.EnrolledDevices d WITH (UPDLOCK,HOLDLOCK)
        JOIN dbo.DocumentSeries ds ON ds.DeviceId=d.DeviceId
        JOIN dbo.Businesses b ON b.BusinessId=ds.BusinessId
        WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
          AND ds.BusinessId=@BusinessId AND ds.IsActive=1 AND b.IsActive=1)
        THROW 51023,N'El equipo no está enrolado y activo en la sede seleccionada.',1;

    IF NOT EXISTS (
        SELECT 1 FROM fiscal.FiscalNumberingPolicies WITH (UPDLOCK,HOLDLOCK)
        WHERE BusinessId=@BusinessId AND DocumentType=N'SalesInvoice')
        INSERT fiscal.FiscalNumberingPolicies(BusinessId,DocumentType,UpdatedAt)
        VALUES(@BusinessId,N'SalesInvoice',@Now);

    DECLARE @BlockSize BIGINT;
    SELECT @BlockSize=BlockSize
    FROM fiscal.FiscalNumberingPolicies WITH (UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND DocumentType=N'SalesInvoice';

    IF @CurrentSeriesId IS NOT NULL AND @NextConsecutive IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM dbo.FiscalSeries
            WHERE SeriesId=@CurrentSeriesId AND BusinessId=@BusinessId
              AND DeviceId=@DeviceId AND EmitterKind=N'Device')
            THROW 51027,N'El cursor fiscal informado no pertenece al equipo.',1;

        IF EXISTS (
            SELECT 1 FROM dbo.FiscalSeries
            WHERE SeriesId=@CurrentSeriesId AND AllocationState=N'Standby'
              AND IsActive=1)
        BEGIN
            UPDATE dbo.FiscalSeries
            SET AllocationState=N'Exhausted',IsActive=0
            WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
              AND DocumentType=N'SalesInvoice' AND AllocationState=N'Active'
              AND SeriesId<>@CurrentSeriesId;
            UPDATE dbo.FiscalSeries
            SET AllocationState=N'Active'
            WHERE SeriesId=@CurrentSeriesId;
        END;

        UPDATE dbo.FiscalSeries
        SET AllocationState=N'Exhausted',IsActive=0
        WHERE SeriesId=@CurrentSeriesId AND @NextConsecutive>RangeEnd;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.FiscalSeries
        WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
          AND DocumentType=N'SalesInvoice' AND EmitterKind=N'Device'
          AND IsActive=1 AND AllocationState=N'Active')
    BEGIN
        UPDATE candidate
        SET AllocationState=N'Active'
        FROM (
            SELECT TOP(1) * FROM dbo.FiscalSeries WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
              AND DocumentType=N'SalesInvoice' AND EmitterKind=N'Device'
              AND IsActive=1 AND AllocationState=N'Standby'
            ORDER BY RangeStart,CreatedAt,SeriesId
        ) candidate;
    END;

    DECLARE @NeedActive BIT = CASE WHEN EXISTS (
        SELECT 1 FROM dbo.FiscalSeries
        WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
          AND DocumentType=N'SalesInvoice' AND EmitterKind=N'Device'
          AND IsActive=1 AND AllocationState=N'Active') THEN 0 ELSE 1 END;
    DECLARE @NeedStandby BIT = CASE WHEN EXISTS (
        SELECT 1 FROM dbo.FiscalSeries
        WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
          AND DocumentType=N'SalesInvoice' AND EmitterKind=N'Device'
          AND IsActive=1 AND AllocationState=N'Standby') THEN 0 ELSE 1 END;

    DECLARE @Pass INT=0;
    WHILE @Pass<2
    BEGIN
        DECLARE @TargetState NVARCHAR(16)=CASE WHEN @Pass=0 THEN N'Active' ELSE N'Standby' END;
        DECLARE @MustAllocate BIT=CASE WHEN @Pass=0 THEN @NeedActive ELSE @NeedStandby END;
        IF @MustAllocate=1
        BEGIN
            DECLARE @PoolId UNIQUEIDENTIFIER=NULL,@AuthorizationId UNIQUEIDENTIFIER=NULL,
                    @Prefix NVARCHAR(16)=NULL,@Start BIGINT=NULL,@PoolEnd BIGINT=NULL,@AssignedEnd BIGINT=NULL,
                    @NewSeriesId UNIQUEIDENTIFIER=CASE WHEN @Pass=0 THEN @ActiveSeriesId ELSE @StandbySeriesId END,
                    @NotificationId UNIQUEIDENTIFIER=CASE WHEN @Pass=0 THEN @ActiveNotificationId ELSE @StandbyNotificationId END,
                    @OutboxCursor BIGINT;
            SELECT TOP(1) @PoolId=s.SeriesId,@AuthorizationId=s.FiscalAuthorizationId,
                   @Prefix=s.Prefix,@Start=s.RangeStart,@PoolEnd=s.RangeEnd
            FROM dbo.FiscalSeries s WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.FiscalAuthorizations a WITH(UPDLOCK,HOLDLOCK)
              ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
            WHERE s.BusinessId=@BusinessId AND s.EmitterKind=N'Device'
              AND s.DeviceId IS NULL AND s.DocumentType=N'SalesInvoice'
              AND s.IsActive=1 AND a.IsActive=1 AND CONVERT(date,@Now)<=a.ValidUntil
            ORDER BY a.ValidFrom,s.RangeStart,s.SeriesId;

            IF @PoolId IS NOT NULL
            BEGIN
                SET @AssignedEnd=CASE WHEN @Start+@BlockSize-1>@PoolEnd THEN @PoolEnd ELSE @Start+@BlockSize-1 END;
                INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                    DocumentType,Prefix,RangeStart,RangeEnd,AllocationState,IsActive,CreatedAt)
                VALUES(@NewSeriesId,@BusinessId,@DeviceId,N'Device',@AuthorizationId,
                    N'SalesInvoice',@Prefix,@Start,@AssignedEnd,@TargetState,1,@Now);
                IF @AssignedEnd=@PoolEnd
                    UPDATE dbo.FiscalSeries SET IsActive=0,AllocationState=N'Exhausted' WHERE SeriesId=@PoolId;
                ELSE
                    UPDATE dbo.FiscalSeries SET RangeStart=@AssignedEnd+1,AllocationState=N'Pool' WHERE SeriesId=@PoolId;
                SELECT @OutboxCursor=ISNULL(MAX(AvailableThroughCursor),0)+1
                FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND Stream=N'FiscalProvisioning';
                INSERT dbo.PosSynchronizationOutboxMessages
                    (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                VALUES(@NotificationId,@BusinessId,N'FiscalProvisioning',@OutboxCursor,@Now);
            END;
        END;
        SET @Pass+=1;
    END;

    SELECT s.SeriesId,s.FiscalAuthorizationId,s.Prefix,a.AuthorizationNumber,
           s.RangeStart,s.RangeEnd,a.ValidFrom,a.ValidUntil,a.Environment,
           a.SupplierTaxId,a.TechnicalKeyVersion,a.QrValidationUrl,
           s.AllocationState,a.AuthorizedRangeStart,a.AuthorizedRangeEnd
    FROM dbo.FiscalSeries s
    JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
    WHERE s.BusinessId=@BusinessId AND s.DeviceId=@DeviceId
      AND s.EmitterKind=N'Device' AND s.DocumentType=N'SalesInvoice'
      AND s.IsActive=1 AND s.AllocationState IN (N'Active',N'Standby')
      AND a.IsActive=1
    ORDER BY CASE s.AllocationState WHEN N'Active' THEN 0 ELSE 1 END,s.RangeStart,s.SeriesId;

END;
GO
