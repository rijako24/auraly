-- =============================================================================
-- MigrateBusinessTimeZone.sql
-- Agrega zona horaria propia por negocio y migra valores legacy desde Google Calendar.
-- =============================================================================

IF COL_LENGTH('dbo.Businesses', 'TimeZone') IS NULL
BEGIN
    ALTER TABLE dbo.Businesses
        ADD [TimeZone] NVARCHAR(100) NOT NULL
            CONSTRAINT DF_Businesses_TimeZone DEFAULT N'America/Bogota';
END
GO

UPDATE b
SET [TimeZone] = COALESCE(NULLIF(LTRIM(RTRIM(JSON_VALUE(ic.SettingsJson, '$.timeZone'))), N''), N'America/Bogota'),
    UpdatedAt = COALESCE(b.UpdatedAt, GETUTCDATE())
FROM dbo.Businesses b
LEFT JOIN dbo.IntegrationConnections ic
    ON ic.BusinessId = b.BusinessId
   AND ic.ConnectionType = 0
   AND ic.Provider = 0
   AND ic.Capability = 0
WHERE NULLIF(LTRIM(RTRIM(b.[TimeZone])), N'') IS NULL
   OR b.[TimeZone] = N'America/Bogota';
GO
