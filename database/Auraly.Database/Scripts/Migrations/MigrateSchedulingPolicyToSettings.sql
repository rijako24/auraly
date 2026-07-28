-- MigrateSchedulingPolicyToSettings
-- Garantiza settings de agenda por negocio.

SET NOCOUNT ON;

MERGE dbo.BusinessSchedulingSettings AS target
USING (
    SELECT BusinessId
    FROM dbo.Businesses
    WHERE IsActive = 1
) AS src
ON target.BusinessId = src.BusinessId
WHEN NOT MATCHED THEN
    INSERT (
        BusinessSchedulingSettingsId,
        BusinessId,
        SlotIntervalMinutes,
        BufferBetweenAppointmentsMinutes,
        MinimumLeadTimeMinutes,
        RequireEmployee,
        EmployeeStrategy,
        CreatedAt)
    VALUES (
        NEWID(),
        src.BusinessId,
        60,
        0,
        0,
        1,
        N'least_versatile',
        GETUTCDATE());

PRINT N'MigrateSchedulingPolicyToSettings: BusinessSchedulingSettings garantizada.';
GO

