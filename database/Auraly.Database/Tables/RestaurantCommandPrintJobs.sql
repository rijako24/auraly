CREATE TABLE [sales].[RestaurantCommandPrintJobs]
(
    [CardId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [ProfileKey] NVARCHAR(64) NOT NULL,
    [State] NVARCHAR(16) NOT NULL,
    [LeaseToken] UNIQUEIDENTIFIER NULL,
    [CopyNumber] INT NOT NULL CONSTRAINT [DF_RestaurantCommandPrintJobs_CopyNumber] DEFAULT (0),
    [PrintKind] NVARCHAR(16) NOT NULL CONSTRAINT [DF_RestaurantCommandPrintJobs_PrintKind] DEFAULT N'Original',
    [CorrectionCancellationId] UNIQUEIDENTIFIER NULL,
    [ClaimedAt] DATETIMEOFFSET(7) NULL,
    [SubmittedAt] DATETIMEOFFSET(7) NULL,
    [LastError] NVARCHAR(500) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantCommandPrintJobs] PRIMARY KEY ([CardId]),
    CONSTRAINT [FK_RestaurantCommandPrintJobs_Card] FOREIGN KEY ([CardId])
        REFERENCES [sales].[RestaurantCommandCards] ([CardId]),
    CONSTRAINT [FK_RestaurantCommandPrintJobs_Device] FOREIGN KEY ([DeviceId],[TenantId])
        REFERENCES [dbo].[EnrolledDevices] ([DeviceId],[TenantId]),
    CONSTRAINT [FK_RestaurantCommandPrintJobs_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantCommandPrintJobs_Correction] FOREIGN KEY ([CorrectionCancellationId])
        REFERENCES [sales].[RestaurantCancellationRequests] ([CancellationId]),
    CONSTRAINT [CK_RestaurantCommandPrintJobs_State] CHECK ([State] IN
        (N'Pending',N'Claimed',N'Submitted',N'Failed',N'Uncertain')),
    CONSTRAINT [CK_RestaurantCommandPrintJobs_CopyNumber] CHECK ([CopyNumber]>=0),
    CONSTRAINT [CK_RestaurantCommandPrintJobs_PrintKind] CHECK
        (([PrintKind]=N'Original' AND [CorrectionCancellationId] IS NULL)
          OR ([PrintKind]=N'Correction' AND [CorrectionCancellationId] IS NOT NULL))
);
GO
CREATE INDEX [IX_RestaurantCommandPrintJobs_Receiver]
    ON [sales].[RestaurantCommandPrintJobs] ([TenantId],[DeviceId],[State],[CreatedAt])
    INCLUDE ([BusinessId],[ProfileKey],[LeaseToken]);
