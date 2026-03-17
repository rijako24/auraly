-- ============================================================
-- Script: RemoveBlobStorageFromIntegrations
-- Elimina blobStorage del JSON de Key=6 (Integrations).
-- El contenedor se calcula en runtime como business-{BusinessId}
-- usando AzureWebJobsStorage; ya no se lee desde BusinessConfiguration.
-- ============================================================

SET NOCOUNT ON;

UPDATE [dbo].[BusinessConfigurations]
SET [Value] = JSON_MODIFY([Value], N'$.blobStorage', NULL),
    [UpdatedAt] = GETUTCDATE()
WHERE [Key] = 6 AND JSON_VALUE([Value], N'$.blobStorage') IS NOT NULL;

PRINT N'blobStorage eliminado de Integrations (Key=6).';
GO
