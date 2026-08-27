CREATE PROCEDURE dbo.DispatchCashDifferenceDocumentCreate
    @JobId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @Sequence BIGINT,
    @DocumentId UNIQUEIDENTIFIER,
    @DocumentType NVARCHAR(64),
    @Now DATETIMEOFFSET(7),
    @Payload NVARCHAR(MAX),
    @PayloadHash BINARY(32)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.DocumentProcessingJobs
        (JobId, BusinessId, ProcessingSequence, DocumentId, DocumentType, Status, AvailableAt, CreatedAt)
    VALUES
        (@JobId, @BusinessId, @Sequence, @DocumentId, @DocumentType, N'Pending', @Now, @Now);

    INSERT dbo.DocumentProcessingPayloads
        (DocumentId, DocumentType, BusinessId, ContractVersion, PayloadJson, PayloadHash, AcceptedAt)
    VALUES
        (@DocumentId, @DocumentType, @BusinessId, 1, @Payload, @PayloadHash, @Now);
END
