CREATE PROCEDURE dbo.AuthenticationEmailOutboxComplete
    @MessageId UNIQUEIDENTIFIER,
    @LeaseId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TenantProvisioningOutboxMessages
    SET ProcessedAt = SYSDATETIMEOFFSET(),
        LeaseId = NULL,
        LeaseExpiresAt = NULL,
        LastError = NULL
    WHERE MessageId = @MessageId
      AND LeaseId = @LeaseId
      AND ProcessedAt IS NULL;
END
