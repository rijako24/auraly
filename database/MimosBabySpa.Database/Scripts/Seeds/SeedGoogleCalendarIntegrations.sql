-- =============================================================================
-- SeedGoogleCalendarIntegrations.sql
--
-- Configura Google Calendar multitenant con credenciales globales en
-- SystemConfigurations(1) y un calendario administrado por negocio.
-- Los calendarId quedan vacios: se crean por API en el primer sync cuando las
-- credenciales globales reales esten configuradas.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @GooglePlatformConfigurationId INT = 1;
DECLARE @DefaultTimeZone NVARCHAR(100) = N'America/Bogota';

DECLARE @PlaceholderCredentials NVARCHAR(MAX) = N'{
  "provider": "Google",
  "ownerEmail": "geraldine.beltran@auralyapp.co",
  "clientId": "",
  "clientSecret": "",
  "refreshToken": "",
  "scopes": "https://www.googleapis.com/auth/calendar"
}';

IF NOT EXISTS (SELECT 1 FROM dbo.SystemConfigurations WHERE SystemConfigurationId = @GooglePlatformConfigurationId)
BEGIN
    INSERT INTO dbo.SystemConfigurations (SystemConfigurationId, [Value], [Description], IsActive, CreatedAt)
    VALUES (
        @GooglePlatformConfigurationId,
        @PlaceholderCredentials,
        N'Credenciales globales de Google Calendar para crear calendarios administrados por Auraly.',
        1,
        GETUTCDATE()
    );
END
ELSE IF EXISTS (
    SELECT 1
    FROM dbo.SystemConfigurations
    WHERE SystemConfigurationId = @GooglePlatformConfigurationId
      AND (
          ISJSON([Value]) <> 1
      )
)
BEGIN
    UPDATE dbo.SystemConfigurations
    SET [Value] = @PlaceholderCredentials,
        [Description] = N'Credenciales globales de Google Calendar para crear calendarios administrados por Auraly.',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE SystemConfigurationId = @GooglePlatformConfigurationId;
END
ELSE
BEGIN
    UPDATE dbo.SystemConfigurations
    SET [Value] = JSON_MODIFY(
            JSON_MODIFY(
                JSON_MODIFY([Value], '$.provider', COALESCE(NULLIF(JSON_VALUE([Value], '$.provider'), N''), N'Google')),
                '$.ownerEmail',
                COALESCE(NULLIF(JSON_VALUE([Value], '$.ownerEmail'), N''), N'geraldine.beltran@auralyapp.co')),
            '$.scopes',
            COALESCE(NULLIF(JSON_VALUE([Value], '$.scopes'), N''), N'https://www.googleapis.com/auth/calendar')),
        [Description] = N'Credenciales globales de Google Calendar para crear calendarios administrados por Auraly.',
        IsActive = 1,
        UpdatedAt = GETUTCDATE()
    WHERE SystemConfigurationId = @GooglePlatformConfigurationId;
END

DECLARE @CalendarIntegrations TABLE
(
    BusinessId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    CalendarSummary NVARCHAR(200) NOT NULL,
    SharedWithEmail NVARCHAR(300) NOT NULL
);

INSERT INTO @CalendarIntegrations (BusinessId, Name, CalendarSummary, SharedWithEmail)
VALUES
    ('22222222-2222-2222-2222-222222222222', N'Google Calendar - Mimos Baby Spa', N'Auraly - Mimos Baby Spa', N'mimosbabyspa@gmail.com'),
    ('A0A10000-0000-0000-0000-000000000001', N'Google Calendar - AURALY', N'Auraly - AURALY', N'richardjacomeg@gmail.com'),
    ('BABA0000-0000-0000-0000-000000000001', N'Google Calendar - Luis Petit', N'Auraly - Luis Petit Profesional Barber', N'cortesluispetit@gmail.com');

MERGE dbo.IntegrationConnections AS target
USING (
    SELECT
        ci.BusinessId,
        CAST(0 AS INT) AS ConnectionType,
        CAST(0 AS INT) AS Provider,
        CAST(0 AS INT) AS Capability,
        ci.Name,
        CAST(NULL AS NVARCHAR(300)) AS AccountIdentifier,
        CAST((
            N'{"calendarId":"","platformConfigurationId":' + CAST(@GooglePlatformConfigurationId AS NVARCHAR(20)) +
            N',"autoCreateCalendar":true,"calendarSummary":"' + ci.CalendarSummary +
            N'","timeZone":"' + @DefaultTimeZone +
            N'","sharedWithEmail":"' + ci.SharedWithEmail +
            N'","sharedRole":"writer","sendSharingNotifications":true,"insertIntoSharedCalendarList":true}'
        ) AS NVARCHAR(MAX)) AS SettingsJson
    FROM @CalendarIntegrations ci
    WHERE EXISTS (SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId = ci.BusinessId)
) AS source
   ON target.BusinessId = source.BusinessId
  AND target.ConnectionType = source.ConnectionType
  AND target.Provider = source.Provider
  AND target.Capability = source.Capability
WHEN MATCHED THEN
    UPDATE SET
        [Name] = source.Name,
        AccountIdentifier = CASE
            WHEN NULLIF(JSON_VALUE(target.SettingsJson, '$.calendarId'), N'') IS NULL THEN source.AccountIdentifier
            ELSE target.AccountIdentifier
        END,
        SettingsJson = CASE
            WHEN NULLIF(JSON_VALUE(target.SettingsJson, '$.calendarId'), N'') IS NULL THEN source.SettingsJson
            ELSE JSON_MODIFY(
                JSON_MODIFY(
                    JSON_MODIFY(
                        JSON_MODIFY(
                            JSON_MODIFY(
                                JSON_MODIFY(
                                    JSON_MODIFY(
                                        JSON_MODIFY(target.SettingsJson, '$.platformConfigurationId', @GooglePlatformConfigurationId),
                                        '$.autoCreateCalendar', CAST(1 AS BIT)),
                                    '$.calendarSummary', JSON_VALUE(source.SettingsJson, '$.calendarSummary')),
                                '$.timeZone', JSON_VALUE(source.SettingsJson, '$.timeZone')),
                            '$.sharedWithEmail', JSON_VALUE(source.SettingsJson, '$.sharedWithEmail')),
                        '$.sharedRole', N'writer'),
                    '$.sendSharingNotifications', CAST(1 AS BIT)),
                '$.insertIntoSharedCalendarList', CAST(1 AS BIT))
        END,
        SecretsJson = NULL,
        IsEnabled = 1,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (IntegrationConnectionId, BusinessId, ConnectionType, Provider, Capability, [Name],
            AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
    VALUES (NEWID(), source.BusinessId, source.ConnectionType, source.Provider, source.Capability, source.Name,
            source.AccountIdentifier, source.SettingsJson, NULL, 1, GETUTCDATE());

PRINT N'SeedGoogleCalendarIntegrations: configuracion Google Calendar multitenant preparada.';
GO

