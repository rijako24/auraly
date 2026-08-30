CREATE PROCEDURE [fiscal].[FiscalResolutionAlertSettingsSave]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @ExpirationWarningDays INT,
    @RemainingNumberWarningThreshold BIGINT,
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.Businesses
        WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
        THROW 51021,N'La sede no pertenece a la empresa autenticada.',1;

    IF @ExpirationWarningDays NOT BETWEEN 0 AND 365
        THROW 51027,N'Los días de alerta deben estar entre 0 y 365.',1;
    IF @RemainingNumberWarningThreshold NOT BETWEEN 0 AND 1000000000
        THROW 51027,N'El umbral de numeración debe estar entre 0 y 1.000.000.000.',1;

    MERGE fiscal.FiscalResolutionAlertSettings WITH(HOLDLOCK) AS target
    USING (SELECT @BusinessId BusinessId) AS source
       ON source.BusinessId=target.BusinessId
    WHEN MATCHED THEN UPDATE SET
        ExpirationWarningDays=@ExpirationWarningDays,
        RemainingNumberWarningThreshold=@RemainingNumberWarningThreshold,
        UpdatedAt=@Now,
        UpdatedByUserId=@UserId
    WHEN NOT MATCHED THEN INSERT(
        BusinessId,ExpirationWarningDays,RemainingNumberWarningThreshold,
        UpdatedAt,UpdatedByUserId)
    VALUES(@BusinessId,@ExpirationWarningDays,@RemainingNumberWarningThreshold,
           @Now,@UserId);
END;
GO
