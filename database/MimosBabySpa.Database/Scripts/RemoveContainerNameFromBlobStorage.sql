-- ============================================================
-- Script: RemoveContainerNameFromBlobStorage
-- Quita containerName del blobStorage en Key=6. El contenedor se calcula como business-{BusinessId}.
-- ============================================================

SET NOCOUNT ON;

UPDATE [dbo].[BusinessConfigurations]
SET [Value] = JSON_MODIFY([Value], N'$.blobStorage', JSON_QUERY(N'{"baseUrl":"https://satalkioaidev.blob.core.windows.net"}')),
    [UpdatedAt] = GETUTCDATE()
WHERE [Key] = 6 AND JSON_VALUE([Value], N'$.blobStorage') IS NOT NULL;

PRINT N'containerName eliminado de blobStorage (Key=6).';
GO
