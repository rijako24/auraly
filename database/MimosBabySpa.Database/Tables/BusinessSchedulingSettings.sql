CREATE TABLE [dbo].[BusinessSchedulingSettings] (
    [BusinessSchedulingSettingsId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SlotIntervalMinutes] INT NOT NULL DEFAULT 60,
    [BufferBetweenAppointmentsMinutes] INT NOT NULL DEFAULT 0,
    [RequireEmployee] BIT NOT NULL DEFAULT 1,
    [EmployeeStrategy] NVARCHAR(50) NOT NULL DEFAULT N'least_versatile',
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_BusinessSchedulingSettings_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE CASCADE,
    CONSTRAINT [CK_BusinessSchedulingSettings_SlotInterval] CHECK ([SlotIntervalMinutes] > 0),
    CONSTRAINT [CK_BusinessSchedulingSettings_Buffer] CHECK ([BufferBetweenAppointmentsMinutes] >= 0)
);

GO

CREATE UNIQUE INDEX [IX_BusinessSchedulingSettings_BusinessId]
    ON [dbo].[BusinessSchedulingSettings] ([BusinessId]);

GO
