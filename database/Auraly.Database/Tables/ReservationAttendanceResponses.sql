CREATE TABLE [dbo].[ReservationAttendanceResponses] (
    [ReservationAttendanceResponseId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ReservationId] UNIQUEIDENTIFIER NOT NULL,
    [SourceJobId] UNIQUEIDENTIFIER NULL,
    [ResponseType] INT NOT NULL,
    [RespondedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [Notes] NVARCHAR(500) NULL,
    CONSTRAINT [FK_ReservationAttendanceResponses_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_ReservationAttendanceResponses_Reservations] FOREIGN KEY ([ReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_ReservationAttendanceResponses_ScheduledAutomationJobs] FOREIGN KEY ([SourceJobId])
        REFERENCES [dbo].[ScheduledAutomationJobs] ([ScheduledAutomationJobId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_ReservationAttendanceResponses_ResponseType] CHECK ([ResponseType] IN (0, 1, 2, 3))
);

GO

CREATE INDEX [IX_ReservationAttendanceResponses_BusinessId_ReservationId_RespondedAtUtc]
    ON [dbo].[ReservationAttendanceResponses] ([BusinessId], [ReservationId], [RespondedAtUtc]);

GO
