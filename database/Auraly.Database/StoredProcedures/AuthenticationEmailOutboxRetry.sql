CREATE PROCEDURE dbo.AuthenticationEmailOutboxRetry
    @MessageId UNIQUEIDENTIFIER,
    @LeaseId UNIQUEIDENTIFIER,
    @Delay INT,
    @Error NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TenantProvisioningOutboxMessages
    SET AvailableAt = DATEADD(SECOND, @Delay, SYSDATETIMEOFFSET()),
        LeaseId = NULL,
        LeaseExpiresAt = NULL,
        LastError = @Error
    WHERE MessageId = @MessageId
      AND LeaseId = @LeaseId
      AND ProcessedAt IS NULL;
END
