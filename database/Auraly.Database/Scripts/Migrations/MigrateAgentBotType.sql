-- Adds the explicit bot purpose to Agents and derives existing records once
-- from their current deterministic configuration.
IF COL_LENGTH('dbo.Agents', 'BotType') IS NULL
BEGIN
    ALTER TABLE dbo.Agents
        ADD BotType INT NOT NULL
            CONSTRAINT DF_Agents_BotType DEFAULT (1) WITH VALUES;
END;

UPDATE dbo.Agents
SET BotType = CASE
    WHEN Kind = N'payment_approval'
         OR SettingsJson LIKE N'%internal.confirm_manual_payment%' THEN 4
    WHEN Kind = N'domicilio' OR SettingsJson LIKE N'%internal.accept_order%'
         OR SettingsJson LIKE N'%internal.reject_order%' THEN 3
    WHEN ISJSON(SettingsJson) = 1
         AND JSON_QUERY(SettingsJson, '$.checkout.modes.order') IS NOT NULL
        THEN 2
    ELSE 1
END
WHERE BotType = 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Agents')
      AND name = N'CK_Agents_BotType'
)
BEGIN
    ALTER TABLE dbo.Agents WITH CHECK
        ADD CONSTRAINT CK_Agents_BotType CHECK (BotType IN (1, 2, 3, 4));
END;

PRINT N'MigrateAgentBotType completed.';
