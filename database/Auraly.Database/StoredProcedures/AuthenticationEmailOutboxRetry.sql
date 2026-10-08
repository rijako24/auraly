CREATE PROCEDURE dbo.AuthenticationEmailOutboxRetry
    @MessageId UNIQUEIDENTIFIER,
    @LeaseId UNIQUEIDENTIFIER,
    @Delay INT,
    @Error NVARCHAR(2000),
    @Permanent BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TenantProvisioningOutboxMessages
    SET AvailableAt = DATEADD(SECOND, @Delay, SYSDATETIMEOFFSET()),
        AttemptCount = CASE WHEN @Permanent = 1 THEN 10 ELSE AttemptCount END,
        LeaseId = NULL,
        LeaseExpiresAt = NULL,
        LastError = @Error
    WHERE MessageId = @MessageId
      AND LeaseId = @LeaseId
      AND ProcessedAt IS NULL;
END
