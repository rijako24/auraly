-- ============================================================
-- Script: UpdateIntegrationsAddBlobStorage
-- Agrega blobStorage (baseUrl) al JSON de Key=6 (Integrations).
-- El contenedor se calcula en runtime como business-{BusinessId}.
-- ============================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @BaseUrl NVARCHAR(500) = N'https://satalkioaidev.blob.core.windows.net';
DECLARE @BlobStorageJson NVARCHAR(300) = N'{"baseUrl":"' + @BaseUrl + N'"}';

-- Actualizar Key=6 agregando blobStorage al JSON existente
UPDATE [dbo].[BusinessConfigurations]
SET [Value] = JSON_MODIFY(ISNULL([Value], N'{}'), N'$.blobStorage', JSON_QUERY(@BlobStorageJson)),
    [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [Key] = 6;

IF @@ROWCOUNT = 0 AND EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)
    INSERT INTO [dbo].[BusinessConfigurations] (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (NEWID(), @BusinessId, 6,
        N'{"blobStorage":{"baseUrl":"' + @BaseUrl + N'"},"googleCalendar":{"enabled":false,"calendarId":"primary"},"wompi":{}}',
        N'Integraciones: Google Calendar, Wompi, Blob Storage', 1, GETUTCDATE());

PRINT N'Integrations (Key=6) actualizado con blobStorage.';
GO
