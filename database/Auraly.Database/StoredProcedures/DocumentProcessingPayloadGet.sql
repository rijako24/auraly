CREATE PROCEDURE [dbo].[DocumentProcessingPayloadGet]
    @DocumentId UNIQUEIDENTIFIER,
    @DocumentType NVARCHAR(80),
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT PayloadJson,AcceptedAt
    FROM dbo.DocumentProcessingPayloads
    WHERE DocumentId=@DocumentId AND DocumentType=@DocumentType AND BusinessId=@BusinessId;
END
