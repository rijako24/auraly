CREATE TABLE [dbo].[BusinessWorkingHours] (
    [BusinessWorkingHourId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DayOfWeek] INT NOT NULL,
    [OpenTime] TIME(0) NOT NULL,
    [CloseTime] TIME(0) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_BusinessWorkingHours_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_BusinessWorkingHours_TimeRange] CHECK ([OpenTime] < [CloseTime]),
    CONSTRAINT [CK_BusinessWorkingHours_DayOfWeek] CHECK ([DayOfWeek] BETWEEN 0 AND 6)
);

GO

CREATE INDEX [IX_BusinessWorkingHours_BusinessId_DayOfWeek_OpenTime]
    ON [dbo].[BusinessWorkingHours] ([BusinessId], [DayOfWeek], [OpenTime]);

GO
