CREATE TABLE [dbo].[EmployeeWorkingHours] (
    [EmployeeWorkingHourId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [EmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [DayOfWeek] INT NOT NULL,
    [OpenTime] TIME(0) NOT NULL,
    [CloseTime] TIME(0) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_EmployeeWorkingHours_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_EmployeeWorkingHours_Employees] FOREIGN KEY ([EmployeeId])
        REFERENCES [dbo].[Employees] ([EmployeeId])
        ON DELETE CASCADE,
    CONSTRAINT [CK_EmployeeWorkingHours_TimeRange] CHECK ([OpenTime] < [CloseTime]),
    CONSTRAINT [CK_EmployeeWorkingHours_DayOfWeek] CHECK ([DayOfWeek] BETWEEN 0 AND 6)
);

GO

CREATE INDEX [IX_EmployeeWorkingHours_BusinessId_EmployeeId_DayOfWeek_OpenTime]
    ON [dbo].[EmployeeWorkingHours] ([BusinessId], [EmployeeId], [DayOfWeek], [OpenTime]);

GO

CREATE INDEX [IX_EmployeeWorkingHours_EmployeeId]
    ON [dbo].[EmployeeWorkingHours] ([EmployeeId]);

GO
