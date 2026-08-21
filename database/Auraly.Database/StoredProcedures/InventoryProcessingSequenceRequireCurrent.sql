CREATE PROCEDURE [dbo].[InventoryProcessingSequenceRequireCurrent]
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
        INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt)
        VALUES(@BusinessId,0,0,SYSDATETIMEOFFSET());
    IF EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND LastAssignedSequence<>LastCompletedSequence)
        THROW 51220, 'El inventario está terminando una operación anterior. Intenta nuevamente.', 1;
END
