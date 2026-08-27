CREATE PROCEDURE dbo.AuthenticationEmailOutboxClaim
    @LeaseId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now DATETIMEOFFSET(7) = SYSDATETIMEOFFSET();

    ;WITH Candidate AS
    (
        SELECT TOP (1) *
        FROM dbo.TenantProvisioningOutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE ProcessedAt IS NULL
          AND Type IN (N'TenantAdministratorInvitation', N'PasswordRecoveryEmail')
          AND AttemptCount < 10
          AND AvailableAt <= @Now
          AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt <= @Now)
        ORDER BY OccurredAt, MessageId
    )
    UPDATE Candidate
    SET LeaseId = @LeaseId,
        LeaseExpiresAt = DATEADD(MINUTE, 2, @Now),
        AttemptCount = AttemptCount + 1,
        LastError = NULL
    OUTPUT inserted.MessageId, inserted.TenantId, inserted.Type, inserted.Payload,
           inserted.AttemptCount, inserted.LeaseId;
END
