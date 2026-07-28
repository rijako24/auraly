CREATE TABLE [dbo].[ServerOutboxMessages]
(
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [Type] NVARCHAR(128) NOT NULL,
    [Payload] NVARCHAR(MAX) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_ServerOutboxMessages_AttemptCount] DEFAULT (0),
    [LastError] NVARCHAR(2000) NULL,
    CONSTRAINT [PK_ServerOutboxMessages] PRIMARY KEY CLUSTERED ([MessageId]),
    CONSTRAINT [FK_ServerOutboxMessages_SalesDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [UQ_ServerOutboxMessages_Document_Type] UNIQUE ([DocumentId], [Type])
);

GO

CREATE INDEX [IX_ServerOutboxMessages_Pending]
    ON [dbo].[ServerOutboxMessages] ([ProcessedAt], [OccurredAt]);

