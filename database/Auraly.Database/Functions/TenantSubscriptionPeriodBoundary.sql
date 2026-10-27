CREATE FUNCTION billing.TenantSubscriptionPeriodBoundary
(
    @Reference DATETIMEOFFSET(7),
    @Months INT,
    @AnchorDay INT
)
RETURNS DATETIMEOFFSET(7)
AS
BEGIN
    DECLARE @Target DATETIMEOFFSET(7)=DATEADD(month,@Months,@Reference);
    DECLARE @LastDay INT=DAY(EOMONTH(@Target));
    RETURN DATEADD(day,CASE WHEN @AnchorDay>@LastDay THEN @LastDay ELSE @AnchorDay END-DAY(@Target),@Target);
END;
GO
