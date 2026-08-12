CREATE TABLE [dbo].[ScheduledAutomationJobs] (
    [ScheduledAutomationJobId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ReservationId] UNIQUEIDENTIFIER NOT NULL,
    [AgentId] UNIQUEIDENTIFIER NOT NULL,
    [JobType] INT NOT NULL,
    [ScheduledAtUtc] DATETIME2 NOT NULL,
    [Status] INT NOT NULL DEFAULT 0,
    [DeduplicationKey] NVARCHAR(300) NOT NULL,
    [Attempts] INT NOT NULL DEFAULT 0,
    [LockedUntilUtc] DATETIME2 NULL,
    [SentAtUtc] DATETIME2 NULL,
    [PayloadJson] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
    [WhatsAppMessageId] NVARCHAR(200) NULL,
    [LastError] NVARCHAR(4000) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ScheduledAutomationJobs_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_ScheduledAutomationJobs_Reservations] FOREIGN KEY ([ReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_ScheduledAutomationJobs_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_ScheduledAutomationJobs_JobType] CHECK ([JobType] IN (0, 1)),
    CONSTRAINT [CK_ScheduledAutomationJobs_Status] CHECK ([Status] IN (0, 1, 2, 3, 4, 5))
);

GO

CREATE UNIQUE INDEX [IX_ScheduledAutomationJobs_DeduplicationKey]
    ON [dbo].[ScheduledAutomationJobs] ([DeduplicationKey]);

GO

CREATE INDEX [IX_ScheduledAutomationJobs_Status_ScheduledAtUtc]
    ON [dbo].[ScheduledAutomationJobs] ([Status], [ScheduledAtUtc]);

GO

CREATE INDEX [IX_ScheduledAutomationJobs_BusinessId_ReservationId_JobType]
    ON [dbo].[ScheduledAutomationJobs] ([BusinessId], [ReservationId], [JobType]);

GO
