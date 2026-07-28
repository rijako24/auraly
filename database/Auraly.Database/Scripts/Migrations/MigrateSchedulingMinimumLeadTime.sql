-- MigrateSchedulingMinimumLeadTime
SET NOCOUNT ON;

IF COL_LENGTH('dbo.BusinessSchedulingSettings', 'MinimumLeadTimeMinutes') IS NULL
BEGIN
    ALTER TABLE dbo.BusinessSchedulingSettings
        ADD MinimumLeadTimeMinutes INT NOT NULL CONSTRAINT DF_BusinessSchedulingSettings_MinimumLeadTimeMinutes DEFAULT 0;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_BusinessSchedulingSettings_MinimumLeadTime'
      AND parent_object_id = OBJECT_ID('dbo.BusinessSchedulingSettings')
)
BEGIN
    ALTER TABLE dbo.BusinessSchedulingSettings
        ADD CONSTRAINT CK_BusinessSchedulingSettings_MinimumLeadTime CHECK (MinimumLeadTimeMinutes >= 0);
END

PRINT N'MigrateSchedulingMinimumLeadTime: BusinessSchedulingSettings.MinimumLeadTimeMinutes garantizada.';
GO