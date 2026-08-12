CREATE TABLE [dbo].[PosSynchronizationOutboxMessages]
(
    [NotificationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Stream] NVARCHAR(32) NOT NULL,
    [AvailableThroughCursor] BIGINT NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [PublishedAt] DATETIMEOFFSET(7) NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_PosSynchronizationOutboxMessages_AttemptCount] DEFAULT (0),
    [LastAttemptAt] DATETIMEOFFSET(7) NULL,
    [LastError] NVARCHAR(1000) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PosSynchronizationOutboxMessages]
        PRIMARY KEY CLUSTERED ([NotificationId]),
    CONSTRAINT [FK_PosSynchronizationOutboxMessages_Business]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_PosSynchronizationOutboxMessages_StreamCursor]
        UNIQUE ([BusinessId], [Stream], [AvailableThroughCursor]),
    CONSTRAINT [CK_PosSynchronizationOutboxMessages_Stream]
        CHECK ([Stream] IN
            (N'Catalog', N'Customers', N'Security', N'FiscalStatus', N'Approvals')),
    CONSTRAINT [CK_PosSynchronizationOutboxMessages_Cursor]
        CHECK ([AvailableThroughCursor] >= 0)
);
GO

CREATE INDEX [IX_PosSynchronizationOutboxMessages_Pending]
    ON [dbo].[PosSynchronizationOutboxMessages]
        ([BusinessId], [Stream], [PublishedAt], [AvailableThroughCursor]);
GO
