CREATE PROCEDURE dbo.DocumentProcessingJobByDocumentGet
    @DocumentId UNIQUEIDENTIFIER,
    @DocumentType NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT JobId
    FROM dbo.DocumentProcessingJobs WITH (UPDLOCK, HOLDLOCK)
    WHERE DocumentId = @DocumentId
      AND DocumentType = @DocumentType;
END
