CREATE TABLE [dbo].[FiscalDocumentProcesses]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [FiscalIssuerConfigurationId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(48) NOT NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_FiscalDocumentProcesses_AttemptCount] DEFAULT (0),
    [TrackId] NVARCHAR(128) NULL,
    [CorrelationId] NVARCHAR(128) NULL,
    [LastStatusCode] NVARCHAR(64) NULL,
    [LastStatusDescription] NVARCHAR(2000) NULL,
    [LastErrorCode] NVARCHAR(128) NULL,
    [LastErrorMessage] NVARCHAR(2000) NULL,
    [NextAttemptAt] DATETIMEOFFSET(7) NULL,
    [LockedAt] DATETIMEOFFSET(7) NULL,
    [LockedBy] NVARCHAR(128) NULL,
    [GeneratedAt] DATETIMEOFFSET(7) NULL,
    [SignedAt] DATETIMEOFFSET(7) NULL,
    [SubmittedAt] DATETIMEOFFSET(7) NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [QuotaBlockedAt] DATETIMEOFFSET(7) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_FiscalDocumentProcesses] PRIMARY KEY CLUSTERED ([DocumentId]),
    CONSTRAINT [FK_FiscalDocumentProcesses_FiscalDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[FiscalDocuments] ([DocumentId]),
    CONSTRAINT [FK_FiscalDocumentProcesses_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_FiscalDocumentProcesses_Configuration] FOREIGN KEY ([FiscalIssuerConfigurationId]) REFERENCES [dbo].[FiscalIssuerConfigurations] ([FiscalIssuerConfigurationId]),
    CONSTRAINT [CK_FiscalDocumentProcesses_AttemptCount] CHECK ([AttemptCount] >= 0)
);

GO

CREATE INDEX [IX_FiscalDocumentProcesses_Business_Status_NextAttempt]
    ON [dbo].[FiscalDocumentProcesses] ([BusinessId], [Status], [NextAttemptAt], [DocumentId]);

GO

CREATE UNIQUE INDEX [UX_FiscalDocumentProcesses_TrackId]
    ON [dbo].[FiscalDocumentProcesses] ([TrackId])
    WHERE [TrackId] IS NOT NULL;
