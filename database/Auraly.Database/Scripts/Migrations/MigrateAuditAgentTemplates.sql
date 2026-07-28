-- =============================================================================
-- MigrateAuditAgentTemplates.sql
--
-- Auditoría post-deploy: agentes activos con tools que emiten plantillas pero
-- sin la clave correspondiente en SettingsJson.templates.
-- No modifica datos; solo imprime advertencias para corrección manual.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @AgentId UNIQUEIDENTIFIER;
DECLARE @Name NVARCHAR(200);
DECLARE @SettingsJson NVARCHAR(MAX);
DECLARE @Missing NVARCHAR(500);

DECLARE agent_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT AgentId, Name, SettingsJson
    FROM dbo.Agents
    WHERE IsActive = 1
      AND SettingsJson IS NOT NULL
      AND LEN(LTRIM(RTRIM(SettingsJson))) > 0;

OPEN agent_cursor;
FETCH NEXT FROM agent_cursor INTO @AgentId, @Name, @SettingsJson;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Missing = NULL;

    IF @SettingsJson LIKE N'%prepare_checkout%'
    BEGIN
        IF @SettingsJson NOT LIKE N'%"checkout_with_deposit"%'
            OR @SettingsJson NOT LIKE N'%checkout_with_deposit%:%'
            SET @Missing = ISNULL(@Missing + N', ', N'') + N'checkout_with_deposit';

        IF @SettingsJson NOT LIKE N'%"checkout_no_deposit"%'
            OR @SettingsJson NOT LIKE N'%checkout_no_deposit%:%'
            SET @Missing = ISNULL(@Missing + N', ', N'') + N'checkout_no_deposit';
    END

    IF @SettingsJson LIKE N'%check_availability%'
    BEGIN
        IF @SettingsJson NOT LIKE N'%"availability_slots"%'
            OR @SettingsJson NOT LIKE N'%availability_slots%:%'
            SET @Missing = ISNULL(@Missing + N', ', N'') + N'availability_slots';
    END

    IF @Missing IS NOT NULL
        PRINT N'MigrateAuditAgentTemplates: agent ''' + @Name + N''' (' + CAST(@AgentId AS NVARCHAR(36))
            + N') missing templates: ' + @Missing;

    FETCH NEXT FROM agent_cursor INTO @AgentId, @Name, @SettingsJson;
END

CLOSE agent_cursor;
DEALLOCATE agent_cursor;

GO
