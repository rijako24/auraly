-- ============================================================
-- MigrateIntegrationsToConnections
-- Copia BusinessConfigurations.Key=Integrations a IntegrationConnections
-- para Google Calendar y Wompi, y elimina la configuracion legacy.
-- ============================================================

SET NOCOUNT ON;

;WITH GoogleCalendar AS (
    SELECT
        BusinessId,
        JSON_VALUE([Value], '$.googleCalendar.enabled') AS Enabled,
        JSON_VALUE([Value], '$.googleCalendar.clientId') AS ClientId,
        JSON_VALUE([Value], '$.googleCalendar.clientSecret') AS ClientSecret,
        JSON_VALUE([Value], '$.googleCalendar.refreshToken') AS RefreshToken,
        COALESCE(NULLIF(JSON_VALUE([Value], '$.googleCalendar.calendarId'), N''), N'primary') AS CalendarId,
        COALESCE(NULLIF(JSON_VALUE([Value], '$.googleCalendar.timeZone'), N''), N'America/Bogota') AS TimeZone,
        JSON_VALUE([Value], '$.googleCalendar.scopes') AS Scopes
    FROM dbo.BusinessConfigurations
    WHERE [Key] = 0 AND ISJSON([Value]) = 1
),
GoogleRows AS (
    SELECT
        BusinessId,
        CAST(0 AS INT) AS Provider,
        CAST(0 AS INT) AS Capability,
        N'Google Calendar' AS [Name],
        NULLIF(CalendarId, N'') AS AccountIdentifier,
        JSON_QUERY((
            SELECT
                CalendarId AS calendarId,
                TimeZone AS timeZone,
                Scopes AS scopes
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )) AS SettingsJson,
        JSON_QUERY((
            SELECT
                ClientId AS clientId,
                ClientSecret AS clientSecret,
                RefreshToken AS refreshToken
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )) AS SecretsJson,
        CASE WHEN LOWER(COALESCE(Enabled, N'false')) = N'true'
               OR NULLIF(RefreshToken, N'') IS NOT NULL
             THEN 1 ELSE 0 END AS IsEnabled
    FROM GoogleCalendar
    WHERE NULLIF(ClientId, N'') IS NOT NULL
       OR NULLIF(ClientSecret, N'') IS NOT NULL
       OR NULLIF(RefreshToken, N'') IS NOT NULL
       OR LOWER(COALESCE(Enabled, N'false')) = N'true'
)
MERGE dbo.IntegrationConnections AS target
USING GoogleRows AS src
   ON target.BusinessId = src.BusinessId
  AND target.Provider = src.Provider
  AND target.Capability = src.Capability
WHEN MATCHED THEN
    UPDATE SET
        [Name] = src.[Name],
        AccountIdentifier = src.AccountIdentifier,
        SettingsJson = src.SettingsJson,
        SecretsJson = src.SecretsJson,
        IsEnabled = src.IsEnabled,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (IntegrationConnectionId, BusinessId, Provider, Capability, [Name], AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
    VALUES (NEWID(), src.BusinessId, src.Provider, src.Capability, src.[Name], src.AccountIdentifier, src.SettingsJson, src.SecretsJson, src.IsEnabled, GETUTCDATE());

;WITH Wompi AS (
    SELECT
        BusinessId,
        JSON_VALUE([Value], '$.wompi.privateKey') AS PrivateKey,
        JSON_VALUE([Value], '$.wompi.publicKey') AS PublicKey,
        JSON_VALUE([Value], '$.wompi.eventsSecret') AS EventsSecret,
        JSON_VALUE([Value], '$.wompi.integritySecret') AS IntegritySecret,
        COALESCE(NULLIF(JSON_VALUE([Value], '$.wompi.mode'), N''), N'test') AS Mode,
        COALESCE(NULLIF(JSON_VALUE([Value], '$.wompi.sandboxBaseUrl'), N''), N'https://sandbox.wompi.co/v1') AS SandboxBaseUrl,
        COALESCE(NULLIF(JSON_VALUE([Value], '$.wompi.productionBaseUrl'), N''), N'https://production.wompi.co/v1') AS ProductionBaseUrl,
        COALESCE(TRY_CONVERT(INT, JSON_VALUE([Value], '$.wompi.requestTimeoutSeconds')), 30) AS RequestTimeoutSeconds,
        COALESCE(NULLIF(JSON_VALUE([Value], '$.wompi.checkoutBaseUrl'), N''), N'https://checkout.wompi.co/l/') AS CheckoutBaseUrl
    FROM dbo.BusinessConfigurations
    WHERE [Key] = 0 AND ISJSON([Value]) = 1
),
WompiRows AS (
    SELECT
        BusinessId,
        CAST(1 AS INT) AS Provider,
        CAST(1 AS INT) AS Capability,
        N'Wompi' AS [Name],
        CAST(NULL AS NVARCHAR(300)) AS AccountIdentifier,
        JSON_QUERY((
            SELECT
                CASE WHEN LOWER(Mode) = N'production' THEN N'production' ELSE N'test' END AS mode,
                SandboxBaseUrl AS sandboxBaseUrl,
                ProductionBaseUrl AS productionBaseUrl,
                RequestTimeoutSeconds AS requestTimeoutSeconds,
                CheckoutBaseUrl AS checkoutBaseUrl
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )) AS SettingsJson,
        JSON_QUERY((
            SELECT
                JSON_QUERY(CASE WHEN LOWER(Mode) = N'production' THEN NULL ELSE (
                    SELECT
                        PrivateKey AS privateKey,
                        PublicKey AS publicKey,
                        EventsSecret AS eventsSecret,
                        IntegritySecret AS integritySecret
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                ) END) AS test,
                JSON_QUERY(CASE WHEN LOWER(Mode) = N'production' THEN (
                    SELECT
                        PrivateKey AS privateKey,
                        PublicKey AS publicKey,
                        EventsSecret AS eventsSecret,
                        IntegritySecret AS integritySecret
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                ) ELSE NULL END) AS production
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )) AS SecretsJson,
        CASE WHEN NULLIF(PrivateKey, N'') IS NOT NULL THEN 1 ELSE 0 END AS IsEnabled
    FROM Wompi
    WHERE NULLIF(PrivateKey, N'') IS NOT NULL
       OR NULLIF(PublicKey, N'') IS NOT NULL
       OR NULLIF(EventsSecret, N'') IS NOT NULL
       OR NULLIF(IntegritySecret, N'') IS NOT NULL
)
MERGE dbo.IntegrationConnections AS target
USING WompiRows AS src
   ON target.BusinessId = src.BusinessId
  AND target.Provider = src.Provider
  AND target.Capability = src.Capability
WHEN MATCHED THEN
    UPDATE SET
        [Name] = src.[Name],
        AccountIdentifier = src.AccountIdentifier,
        SettingsJson = src.SettingsJson,
        SecretsJson = src.SecretsJson,
        IsEnabled = src.IsEnabled,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (IntegrationConnectionId, BusinessId, Provider, Capability, [Name], AccountIdentifier, SettingsJson, SecretsJson, IsEnabled, CreatedAt)
    VALUES (NEWID(), src.BusinessId, src.Provider, src.Capability, src.[Name], src.AccountIdentifier, src.SettingsJson, src.SecretsJson, src.IsEnabled, GETUTCDATE());

;WITH WompiConnection AS (
    SELECT
        IntegrationConnectionId,
        CASE
            WHEN LOWER(JSON_VALUE(SettingsJson, '$.mode')) = N'production' THEN N'production'
            WHEN LOWER(JSON_VALUE(SettingsJson, '$.' + N'use' + N'Sandbox')) = N'false' THEN N'production'
            ELSE N'test'
        END AS Mode,
        COALESCE(NULLIF(JSON_VALUE(SettingsJson, '$.sandboxBaseUrl'), N''), N'https://sandbox.wompi.co/v1') AS SandboxBaseUrl,
        COALESCE(NULLIF(JSON_VALUE(SettingsJson, '$.productionBaseUrl'), N''), N'https://production.wompi.co/v1') AS ProductionBaseUrl,
        COALESCE(TRY_CONVERT(INT, JSON_VALUE(SettingsJson, '$.requestTimeoutSeconds')), 30) AS RequestTimeoutSeconds,
        COALESCE(NULLIF(JSON_VALUE(SettingsJson, '$.checkoutBaseUrl'), N''), N'https://checkout.wompi.co/l/') AS CheckoutBaseUrl,
        SecretsJson
    FROM dbo.IntegrationConnections
    WHERE ConnectionType = 0
      AND Provider = 1
      AND Capability = 1
),
WompiNormalized AS (
    SELECT
        IntegrationConnectionId,
        JSON_QUERY((
            SELECT
                Mode AS mode,
                SandboxBaseUrl AS sandboxBaseUrl,
                ProductionBaseUrl AS productionBaseUrl,
                RequestTimeoutSeconds AS requestTimeoutSeconds,
                CheckoutBaseUrl AS checkoutBaseUrl
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )) AS SettingsJson,
        JSON_QUERY((
            SELECT
                JSON_QUERY(CASE
                    WHEN JSON_VALUE(SecretsJson, '$.test.privateKey') IS NOT NULL THEN JSON_QUERY(SecretsJson, '$.test')
                    WHEN Mode = N'test' THEN (
                        SELECT
                            JSON_VALUE(SecretsJson, '$.privateKey') AS privateKey,
                            JSON_VALUE(SecretsJson, '$.publicKey') AS publicKey,
                            JSON_VALUE(SecretsJson, '$.eventsSecret') AS eventsSecret,
                            JSON_VALUE(SecretsJson, '$.integritySecret') AS integritySecret
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                    )
                    ELSE NULL
                END) AS test,
                JSON_QUERY(CASE
                    WHEN JSON_VALUE(SecretsJson, '$.production.privateKey') IS NOT NULL THEN JSON_QUERY(SecretsJson, '$.production')
                    WHEN Mode = N'production' THEN (
                        SELECT
                            JSON_VALUE(SecretsJson, '$.privateKey') AS privateKey,
                            JSON_VALUE(SecretsJson, '$.publicKey') AS publicKey,
                            JSON_VALUE(SecretsJson, '$.eventsSecret') AS eventsSecret,
                            JSON_VALUE(SecretsJson, '$.integritySecret') AS integritySecret
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                    )
                    ELSE NULL
                END) AS production
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )) AS SecretsJson
    FROM WompiConnection
)
UPDATE target
SET AccountIdentifier = NULL,
    SettingsJson = src.SettingsJson,
    SecretsJson = src.SecretsJson,
    UpdatedAt = GETUTCDATE()
FROM dbo.IntegrationConnections AS target
JOIN WompiNormalized AS src
  ON src.IntegrationConnectionId = target.IntegrationConnectionId;

DELETE FROM dbo.BusinessConfigurations
WHERE [Key] = 0;

PRINT N'MigrateIntegrationsToConnections: integraciones migradas a IntegrationConnections.';
GO
