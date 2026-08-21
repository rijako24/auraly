CREATE PROCEDURE [dbo].[DocumentProcessingComplete]
    @JobId UNIQUEIDENTIFIER,
    @Sequence BIGINT,
    @BusinessId UNIQUEIDENTIFIER,
    @CompletedAt DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DocumentProcessingJobs
    SET Status=N'Completed',CompletedAt=@CompletedAt,LeaseOwner=NULL,LeaseExpiresAt=NULL,LastError=NULL
    WHERE JobId=@JobId AND Status=N'Pending';
    IF @@ROWCOUNT <> 1 THROW 51221, 'No se pudo completar el trabajo de inventario.', 1;
    UPDATE dbo.BusinessProcessingCursors
    SET LastCompletedSequence=@Sequence,UpdatedAt=@CompletedAt
    WHERE BusinessId=@BusinessId AND LastCompletedSequence=@Sequence-1;
    IF @@ROWCOUNT <> 1 THROW 51222, 'No se pudo completar la secuencia de inventario.', 1;
END
