CREATE TABLE [dbo].[ServerOutboxMessages]
(
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(64) NOT NULL CONSTRAINT [DF_ServerOutboxMessages_DocumentType] DEFAULT (N'SalesInvoice'),
    [Type] NVARCHAR(128) NOT NULL,
    [Payload] NVARCHAR(MAX) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_ServerOutboxMessages_AttemptCount] DEFAULT (0),
    [LastError] NVARCHAR(2000) NULL,
    [RecoveryAvailableAt] DATETIMEOFFSET(7) NULL,
    [RecoveryLeaseId] UNIQUEIDENTIFIER NULL,
    [RecoveryPublicationCount] INT NOT NULL CONSTRAINT [DF_ServerOutboxMessages_RecoveryPublicationCount] DEFAULT (0),
    CONSTRAINT [PK_ServerOutboxMessages] PRIMARY KEY CLUSTERED ([MessageId]),
    CONSTRAINT [FK_ServerOutboxMessages_DocumentJob] FOREIGN KEY ([DocumentId], [DocumentType]) REFERENCES [dbo].[DocumentProcessingJobs] ([DocumentId], [DocumentType]),
    CONSTRAINT [UQ_ServerOutboxMessages_Document_Type] UNIQUE ([DocumentId], [DocumentType], [Type])
);

GO

CREATE INDEX [IX_ServerOutboxMessages_Pending]
    ON [dbo].[ServerOutboxMessages] ([ProcessedAt], [OccurredAt]);

GO

CREATE INDEX [IX_ServerOutboxMessages_RecoveryPending]
    ON [dbo].[ServerOutboxMessages] ([RecoveryAvailableAt], [MessageId])
    INCLUDE ([ProcessedAt], [DocumentId], [DocumentType], [RecoveryPublicationCount])
    WHERE [RecoveryAvailableAt] IS NOT NULL AND [ProcessedAt] IS NULL;

GO

CREATE UNIQUE INDEX [UX_ServerOutboxMessages_RecoveryDocument]
    ON [dbo].[ServerOutboxMessages] ([DocumentId], [DocumentType])
    WHERE [RecoveryAvailableAt] IS NOT NULL;

