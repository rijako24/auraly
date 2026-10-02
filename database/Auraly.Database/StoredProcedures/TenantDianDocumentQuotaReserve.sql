CREATE PROCEDURE dbo.TenantDianDocumentQuotaReserve
    @BusinessId UNIQUEIDENTIFIER,
    @DocumentId UNIQUEIDENTIFIER,
    @DocumentKind NVARCHAR(32),
    @Now DATETIMEOFFSET(7),
    @Reserved BIT OUTPUT,
    @DocumentIdsJson NVARCHAR(MAX) = NULL,
    @FailureReason NVARCHAR(32) = NULL OUTPUT,
    @Release BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Reserved=0;
    SET @FailureReason=NULL;

    IF @Release=1
    BEGIN
        IF @DocumentIdsJson IS NOT NULL THROW 51733,N'Quota release accepts one document.',1;
        DECLARE @Released TABLE(PeriodId uniqueidentifier);
        UPDATE usageValue
        SET Status=N'Released',ReleasedAt=@Now
        OUTPUT inserted.TenantSubscriptionUsagePeriodId INTO @Released
        FROM billing.TenantDianDocumentUsages usageValue
        WHERE usageValue.BusinessId=@BusinessId
          AND usageValue.SourceDocumentId=@DocumentId
          AND usageValue.DocumentKind=@DocumentKind
          AND usageValue.Status=N'Reserved'
          AND NOT EXISTS(SELECT 1 FROM dbo.SalesDocuments document
                         WHERE document.DocumentId=usageValue.SourceDocumentId);
        DECLARE @ReleasedCount int=(SELECT COUNT(*) FROM @Released);
        UPDATE periodValue
        SET DianDocumentsUsed=DianDocumentsUsed-1,UpdatedAt=@Now
        FROM billing.TenantSubscriptionUsagePeriods periodValue
        JOIN @Released released ON released.PeriodId=periodValue.TenantSubscriptionUsagePeriodId
        WHERE periodValue.DianDocumentsUsed>0;
        IF @ReleasedCount>0 AND @@ROWCOUNT<>@ReleasedCount
            THROW 51734,N'Quota release could not update its usage period.',1;
        SET @Reserved=1;
        RETURN;
    END;

    DECLARE @TenantId UNIQUEIDENTIFIER,@SubscriptionId UNIQUEIDENTIFIER,
            @PeriodId UNIQUEIDENTIFIER,@Limit INT,@Used INT,@SubscriptionStatus NVARCHAR(24),
            @CurrentStart DATETIMEOFFSET(7),@CurrentEnd DATETIMEOFFSET(7),
            @GraceDays INT,@PeriodStart DATETIMEOFFSET(7),@PeriodEnd DATETIMEOFFSET(7),
            @MonthOffset INT;
    SELECT @TenantId=business.TenantId
    FROM dbo.Businesses business WITH (UPDLOCK,HOLDLOCK)
    JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId AND tenant.IsActive=1
    WHERE business.BusinessId=@BusinessId AND business.IsActive=1;
    IF @TenantId IS NULL BEGIN SET @FailureReason=N'TenantInactive'; RETURN; END;

    DECLARE @Pending TABLE(DocumentId uniqueidentifier PRIMARY KEY);
    IF @DocumentIdsJson IS NULL INSERT @Pending VALUES(@DocumentId);
    ELSE
    BEGIN
        IF ISJSON(@DocumentIdsJson)<>1 THROW 51733,N'Invalid document quota batch.',1;
        INSERT @Pending SELECT CONVERT(uniqueidentifier,value) FROM OPENJSON(@DocumentIdsJson);
        IF (SELECT COUNT(*) FROM @Pending) NOT BETWEEN 1 AND 100
          THROW 51733,N'Invalid document quota batch size.',1;
    END;
    DELETE p FROM @Pending p WHERE EXISTS(
      SELECT 1 FROM billing.TenantDianDocumentUsages WITH(UPDLOCK,HOLDLOCK)
      WHERE TenantId=@TenantId AND SourceDocumentId=p.DocumentId
        AND DocumentKind=@DocumentKind AND Status=N'Reserved');
    DECLARE @Count int=(SELECT COUNT(*) FROM @Pending);
    IF @Count=0 BEGIN SET @Reserved=1; RETURN; END;

    SELECT @SubscriptionId=TenantSubscriptionId,@Limit=DianDocumentMonthlyLimit,
           @SubscriptionStatus=Status,@CurrentStart=CurrentPeriodStart,
           @CurrentEnd=CurrentPeriodEnd
    FROM billing.TenantSubscriptions WITH (UPDLOCK,HOLDLOCK)
    WHERE TenantId=@TenantId;
    IF @SubscriptionId IS NULL
    BEGIN
        -- Compatibilidad transitoria para Auraly y tenants anteriores al modelo comercial.
        -- Una suscripción existente siempre se valida de forma estricta.
        SET @Reserved=1;
        RETURN;
    END;
    SELECT @GraceDays=GracePeriodDays FROM billing.PlatformBillingSettings
    WHERE PlatformBillingSettingId=1;
    IF @GraceDays IS NULL BEGIN SET @FailureReason=N'PeriodUnavailable'; RETURN; END;
    IF @SubscriptionStatus NOT IN(N'Active',N'PastDue')
       OR @Now>=DATEADD(day,@GraceDays,@CurrentEnd)
    BEGIN SET @FailureReason=N'SubscriptionInactive'; RETURN; END;
    SELECT @PeriodId=TenantSubscriptionUsagePeriodId,@Used=DianDocumentsUsed
    FROM billing.TenantSubscriptionUsagePeriods WITH (UPDLOCK,HOLDLOCK)
    WHERE TenantSubscriptionId=@SubscriptionId AND PeriodStart<=@Now AND PeriodEnd>@Now;
    IF @PeriodId IS NULL
    BEGIN
        IF @Now<@CurrentStart
        BEGIN SET @FailureReason=N'PeriodUnavailable'; RETURN; END;
        IF @Now>=@CurrentEnd
        BEGIN
            SET @PeriodStart=@CurrentEnd;
            SET @PeriodEnd=DATEADD(month,1,@CurrentEnd);
        END
        ELSE
        BEGIN
            SET @MonthOffset=DATEDIFF(month,@CurrentStart,@Now);
            SET @PeriodStart=DATEADD(month,@MonthOffset,@CurrentStart);
            IF @PeriodStart>@Now
            BEGIN
                SET @MonthOffset=@MonthOffset-1;
                SET @PeriodStart=DATEADD(month,@MonthOffset,@CurrentStart);
            END;
            SET @PeriodEnd=DATEADD(month,@MonthOffset+1,@CurrentStart);
            IF @PeriodEnd>@CurrentEnd SET @PeriodEnd=@CurrentEnd;
        END;
        IF @PeriodEnd<=@Now
        BEGIN SET @FailureReason=N'PeriodUnavailable'; RETURN; END;
        INSERT billing.TenantSubscriptionUsagePeriods
          (TenantSubscriptionUsagePeriodId,TenantSubscriptionId,PeriodStart,PeriodEnd,
           DianDocumentsUsed,CreatedAt,UpdatedAt)
        VALUES(NEWID(),@SubscriptionId,@PeriodStart,@PeriodEnd,0,@Now,@Now);
        SELECT @PeriodId=TenantSubscriptionUsagePeriodId,@Used=DianDocumentsUsed
        FROM billing.TenantSubscriptionUsagePeriods WITH (UPDLOCK,HOLDLOCK)
        WHERE TenantSubscriptionId=@SubscriptionId AND PeriodStart=@PeriodStart;
    END;
    IF @Used>@Limit-@Count BEGIN SET @FailureReason=N'QuotaExhausted'; RETURN; END;

    UPDATE billing.TenantSubscriptionUsagePeriods
    SET DianDocumentsUsed=DianDocumentsUsed+@Count,UpdatedAt=@Now
    WHERE TenantSubscriptionUsagePeriodId=@PeriodId AND DianDocumentsUsed<=@Limit-@Count;
    IF @@ROWCOUNT<>1 BEGIN SET @FailureReason=N'QuotaExhausted'; RETURN; END;

    INSERT billing.TenantDianDocumentUsages
      (TenantDianDocumentUsageId,TenantSubscriptionUsagePeriodId,TenantId,BusinessId,
       SourceDocumentId,DocumentKind,Status,ReservedAt)
    SELECT NEWID(),@PeriodId,@TenantId,@BusinessId,DocumentId,@DocumentKind,N'Reserved',@Now FROM @Pending;
    SET @Reserved=1;
END;
GO
