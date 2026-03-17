-- ============================================================
-- Script: UpdateBusinessAttachmentsBlobPathToFullUrl
-- Actualiza BlobPath con la URL completa para los adjuntos del negocio 22222222-2222-2222-2222-222222222222.
-- Ejecutar: sqlcmd -S .\LOCAL -d talkioai -U admin -P masterkey -C -i "UpdateBusinessAttachmentsBlobPathToFullUrl.sql"
-- ============================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @BaseUrl NVARCHAR(500) = N'https://satalkioaidev.blob.core.windows.net/business-22222222-2222-2222-2222-222222222222';

UPDATE [dbo].[BusinessAttachments]
SET [BlobPath] = @BaseUrl + N'/' + [BlobPath]
WHERE [BusinessId] = @BusinessId
  AND [BlobPath] NOT LIKE N'https://%';

DECLARE @Updated INT = @@ROWCOUNT;
PRINT N'BlobPath actualizado: ' + CAST(@Updated AS NVARCHAR(10)) + N' registro(s).';
GO
