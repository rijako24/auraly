CREATE TABLE [dbo].[EmployeeServices] (
    [EmployeeServiceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [EmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [ServiceId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_EmployeeServices_Employees] FOREIGN KEY ([EmployeeId])
        REFERENCES [dbo].[Employees] ([EmployeeId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_EmployeeServices_Services] FOREIGN KEY ([ServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_EmployeeServices_EmployeeId_ServiceId] ON [dbo].[EmployeeServices] ([EmployeeId], [ServiceId]);

GO

CREATE INDEX [IX_EmployeeServices_EmployeeId] ON [dbo].[EmployeeServices] ([EmployeeId]);

GO

CREATE INDEX [IX_EmployeeServices_ServiceId] ON [dbo].[EmployeeServices] ([ServiceId]);

GO
