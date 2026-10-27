CREATE PROCEDURE dbo.TenantSubscriptionSuspensionGet
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(bit,CASE WHEN EXISTS(
        SELECT 1 FROM dbo.Tenants
        WHERE TenantId=@TenantId AND IsActive=0)
        THEN 1 ELSE 0 END);
END;
GO
