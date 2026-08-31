CREATE PROCEDURE dbo.TenantSubscriptionSuspensionGet
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(bit,CASE WHEN EXISTS(
        SELECT 1 FROM billing.TenantSubscriptions
        WHERE TenantId=@TenantId AND Status=N'Suspended') THEN 1 ELSE 0 END);
END;
GO
