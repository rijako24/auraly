CREATE PROCEDURE dbo.AuthenticationEmailOutboxComplete
    @MessageId UNIQUEIDENTIFIER,
    @LeaseId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @CompletedAt DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();
    UPDATE dbo.TenantProvisioningOutboxMessages
    SET ProcessedAt = @CompletedAt,
        LeaseId = NULL,
        LeaseExpiresAt = NULL,
        LastError = NULL
    WHERE MessageId = @MessageId
      AND LeaseId = @LeaseId
      AND ProcessedAt IS NULL;
    IF @@ROWCOUNT=1
      UPDATE dbo.FiscalDocuments
      SET DeliveredAt=@CompletedAt
      WHERE DeliveryOutboxMessageId=@MessageId AND DeliveredAt IS NULL;
END
