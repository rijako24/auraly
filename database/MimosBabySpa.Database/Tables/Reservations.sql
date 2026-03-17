CREATE TABLE [dbo].[Reservations] (
    [ReservationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ServiceId] UNIQUEIDENTIFIER NOT NULL,
    [EmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NULL,
    [ReservationDateTime] DATETIME2 NOT NULL,
    [DurationMinutes] INT NOT NULL,
    [Status] INT NOT NULL,
    [CalendarEventId] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Reservations_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Reservations_Services] FOREIGN KEY ([ServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Reservations_Employees] FOREIGN KEY ([EmployeeId])
        REFERENCES [dbo].[Employees] ([EmployeeId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Reservations_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE SET NULL
);

GO

CREATE INDEX [IX_Reservations_BusinessId] ON [dbo].[Reservations] ([BusinessId]);

GO

CREATE INDEX [IX_Reservations_ServiceId] ON [dbo].[Reservations] ([ServiceId]);

GO

CREATE INDEX [IX_Reservations_EmployeeId] ON [dbo].[Reservations] ([EmployeeId]);

GO

CREATE INDEX [IX_Reservations_ConversationId] ON [dbo].[Reservations] ([ConversationId]);

GO

CREATE INDEX [IX_Reservations_BusinessId_ReservationDateTime] ON [dbo].[Reservations] ([BusinessId], [ReservationDateTime]);

GO

CREATE INDEX [IX_Reservations_EmployeeId_ReservationDateTime] ON [dbo].[Reservations] ([EmployeeId], [ReservationDateTime]);

GO
