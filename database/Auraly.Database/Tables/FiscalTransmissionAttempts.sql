CREATE TABLE [dbo].[FiscalTransmissionAttempts]
(
    [FiscalTransmissionAttemptId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [AttemptNumber] INT NOT NULL,
    [Operation] NVARCHAR(32) NOT NULL,
    [CorrelationId] NVARCHAR(128) NOT NULL,
    [TrackId] NVARCHAR(128) NULL,
    [StartedAt] DATETIMEOFFSET(7) NOT NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [Disposition] NVARCHAR(32) NULL,
    [StatusCode] NVARCHAR(64) NULL,
    [StatusDescription] NVARCHAR(2000) NULL,
    [MayHaveReachedDian] BIT NOT NULL,
    [RequestArtifactId] UNIQUEIDENTIFIER NULL,
    [ResponseArtifactId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_FiscalTransmissionAttempts] PRIMARY KEY CLUSTERED ([FiscalTransmissionAttemptId]),
    CONSTRAINT [FK_FiscalTransmissionAttempts_FiscalDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[FiscalDocuments] ([DocumentId]),
    CONSTRAINT [FK_FiscalTransmissionAttempts_RequestArtifact] FOREIGN KEY ([RequestArtifactId]) REFERENCES [dbo].[FiscalArtifacts] ([FiscalArtifactId]),
    CONSTRAINT [FK_FiscalTransmissionAttempts_ResponseArtifact] FOREIGN KEY ([ResponseArtifactId]) REFERENCES [dbo].[FiscalArtifacts] ([FiscalArtifactId]),
    CONSTRAINT [UQ_FiscalTransmissionAttempts_Document_Number] UNIQUE ([DocumentId], [AttemptNumber]),
    CONSTRAINT [UQ_FiscalTransmissionAttempts_Correlation] UNIQUE ([CorrelationId]),
    CONSTRAINT [CK_FiscalTransmissionAttempts_Number] CHECK ([AttemptNumber] > 0),
    CONSTRAINT [CK_FiscalTransmissionAttempts_Operation] CHECK ([Operation] IN ('SendTestSetAsync', 'GetStatusZip', 'SendBillSync'))
);

GO

CREATE INDEX [IX_FiscalTransmissionAttempts_Document_Started]
    ON [dbo].[FiscalTransmissionAttempts] ([DocumentId], [StartedAt]);
