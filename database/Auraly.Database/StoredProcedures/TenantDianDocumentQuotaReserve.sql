CREATE PROCEDURE dbo.TenantDianDocumentQuotaReserve
    @BusinessId UNIQUEIDENTIFIER,
    @DocumentId UNIQUEIDENTIFIER,
    @DocumentKind NVARCHAR(32),
    @Now DATETIMEOFFSET(7),
    @Reserved BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Reserved=0;

    DECLARE @TenantId UNIQUEIDENTIFIER,@SubscriptionId UNIQUEIDENTIFIER,
            @PeriodId UNIQUEIDENTIFIER,@Limit INT,@Used INT,@SubscriptionStatus NVARCHAR(16);
    SELECT @TenantId=TenantId FROM dbo.Businesses WITH (UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND IsActive=1;
    IF @TenantId IS NULL RETURN;

    IF EXISTS(SELECT 1 FROM billing.TenantDianDocumentUsages WITH (UPDLOCK,HOLDLOCK)
              WHERE TenantId=@TenantId AND SourceDocumentId=@DocumentId
                AND DocumentKind=@DocumentKind AND Status=N'Reserved')
    BEGIN SET @Reserved=1; RETURN; END;

    SELECT @SubscriptionId=TenantSubscriptionId,@Limit=DianDocumentMonthlyLimit,
           @SubscriptionStatus=Status
    FROM billing.TenantSubscriptions WITH (UPDLOCK,HOLDLOCK)
    WHERE TenantId=@TenantId;
    IF @SubscriptionId IS NULL
    BEGIN
        -- Compatibilidad transitoria para Auraly y tenants anteriores al modelo comercial.
        -- Una suscripción existente siempre se valida de forma estricta.
        SET @Reserved=1;
        RETURN;
    END;
    IF @SubscriptionStatus NOT IN(N'Active',N'PastDue') RETURN;
    SELECT @PeriodId=TenantSubscriptionUsagePeriodId,@Used=DianDocumentsUsed
    FROM billing.TenantSubscriptionUsagePeriods WITH (UPDLOCK,HOLDLOCK)
    WHERE TenantSubscriptionId=@SubscriptionId AND PeriodStart<=@Now AND PeriodEnd>@Now;
    IF @PeriodId IS NULL OR @Used>=@Limit RETURN;

    UPDATE billing.TenantSubscriptionUsagePeriods
    SET DianDocumentsUsed=DianDocumentsUsed+1,UpdatedAt=@Now
    WHERE TenantSubscriptionUsagePeriodId=@PeriodId AND DianDocumentsUsed<@Limit;
    IF @@ROWCOUNT<>1 RETURN;

    INSERT billing.TenantDianDocumentUsages
      (TenantDianDocumentUsageId,TenantSubscriptionUsagePeriodId,TenantId,BusinessId,
       SourceDocumentId,DocumentKind,Status,ReservedAt)
    VALUES(NEWID(),@PeriodId,@TenantId,@BusinessId,@DocumentId,@DocumentKind,N'Reserved',@Now);
    SET @Reserved=1;
END;
GO
