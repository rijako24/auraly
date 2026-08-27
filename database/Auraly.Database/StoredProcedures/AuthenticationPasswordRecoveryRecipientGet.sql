CREATE PROCEDURE dbo.AuthenticationPasswordRecoveryRecipientGet
    @RequestId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT appUser.Email, appUser.FirstName, tenant.Name
    FROM dbo.PasswordResetRequests resetRequest
    INNER JOIN dbo.AppUsers appUser
        ON appUser.UserId = resetRequest.UserId
       AND appUser.TenantId = resetRequest.TenantId
    INNER JOIN dbo.Tenants tenant ON tenant.TenantId = resetRequest.TenantId
    WHERE resetRequest.PasswordResetRequestId = @RequestId
      AND resetRequest.Status = N'Pending'
      AND resetRequest.ExpiresAt > SYSDATETIMEOFFSET();
END
