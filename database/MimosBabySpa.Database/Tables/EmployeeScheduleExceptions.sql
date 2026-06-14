CREATE TABLE [dbo].[EmployeeScheduleExceptions] (
    [EmployeeScheduleExceptionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [EmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [Date] DATE NOT NULL,
    [OpenTime] TIME(0) NULL,
    [CloseTime] TIME(0) NULL,
    [IsClosed] BIT NOT NULL DEFAULT 0,
    [Reason] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_EmployeeScheduleExceptions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_EmployeeScheduleExceptions_Employees] FOREIGN KEY ([EmployeeId])
        REFERENCES [dbo].[Employees] ([EmployeeId])
        ON DELETE CASCADE,
    CONSTRAINT [CK_EmployeeScheduleExceptions_TimeRange] CHECK (
        ([OpenTime] IS NULL AND [CloseTime] IS NULL)
        OR ([OpenTime] IS NOT NULL AND [CloseTime] IS NOT NULL AND [OpenTime] < [CloseTime])
    )
);

GO

CREATE INDEX [IX_EmployeeScheduleExceptions_BusinessId_EmployeeId_Date]
    ON [dbo].[EmployeeScheduleExceptions] ([BusinessId], [EmployeeId], [Date]);

GO
