CREATE PROCEDURE dbo.AuthenticationInvitationRecipientGet
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Name
    FROM dbo.Tenants
    WHERE TenantId = @TenantId;
END
