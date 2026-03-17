-- ============================================================
-- Script: RevertBlobPathToRelative
-- Deja BlobPath solo con el nombre relativo (quita URL completa).
-- Para negocio 22222222-2222-2222-2222-222222222222.
-- ============================================================

SET NOCOUNT ON;

DECLARE @Prefix NVARCHAR(600) = N'https://satalkioaidev.blob.core.windows.net/business-22222222-2222-2222-2222-222222222222/';

UPDATE [dbo].[BusinessAttachments]
SET [BlobPath] = SUBSTRING([BlobPath], LEN(@Prefix) + 1, 500)
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222'
  AND [BlobPath] LIKE @Prefix + N'%';

DECLARE @Updated INT = @@ROWCOUNT;
PRINT N'BlobPath revertido a relativo: ' + CAST(@Updated AS NVARCHAR(10)) + N' registro(s).';
GO
