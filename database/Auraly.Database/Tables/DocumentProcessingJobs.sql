CREATE TABLE [dbo].[DocumentProcessingJobs]
(
    [JobId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProcessingSequence] BIGINT NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(64) NOT NULL,
    [Status] NVARCHAR(32) NOT NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_DocumentProcessingJobs_AttemptCount] DEFAULT (0),
    [AvailableAt] DATETIMEOFFSET(7) NOT NULL,
    [LeaseOwner] NVARCHAR(160) NULL,
    [LeaseExpiresAt] DATETIMEOFFSET(7) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [StartedAt] DATETIMEOFFSET(7) NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [LastError] NVARCHAR(2000) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_DocumentProcessingJobs] PRIMARY KEY CLUSTERED ([JobId]),
    CONSTRAINT [FK_DocumentProcessingJobs_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_DocumentProcessingJobs_BusinessSequence]
        UNIQUE ([BusinessId], [ProcessingSequence]),
    CONSTRAINT [UQ_DocumentProcessingJobs_Document]
        UNIQUE ([DocumentId], [DocumentType]),
    CONSTRAINT [CK_DocumentProcessingJobs_Sequence] CHECK ([ProcessingSequence] > 0),
    CONSTRAINT [CK_DocumentProcessingJobs_Attempts] CHECK ([AttemptCount] >= 0),
    CONSTRAINT [CK_DocumentProcessingJobs_Status]
        CHECK ([Status] IN (N'Pending', N'Processing', N'Completed', N'RetryScheduled', N'NeedsIntervention'))
);

GO

CREATE INDEX [IX_DocumentProcessingJobs_Dispatch]
    ON [dbo].[DocumentProcessingJobs] ([BusinessId], [Status], [ProcessingSequence])
    INCLUDE ([AvailableAt], [LeaseExpiresAt], [DocumentId], [DocumentType]);

