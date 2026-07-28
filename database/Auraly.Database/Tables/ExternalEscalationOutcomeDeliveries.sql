CREATE TABLE [dbo].[ExternalEscalationOutcomeDeliveries] (
    [ExternalEscalationOutcomeDeliveryId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ExternalEscalationAttemptId] UNIQUEIDENTIFIER NOT NULL,
    [OutcomeKey] NVARCHAR(100) NOT NULL,
    [PayloadJson] NVARCHAR(MAX) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL,
    [NextAttemptAt] DATETIME2 NOT NULL,
    [LastAttemptAt] DATETIME2 NULL,
    [PublishedAt] DATETIME2 NULL,
    [PublishAttempts] INT NOT NULL CONSTRAINT [DF_ExternalEscalationOutcomeDeliveries_PublishAttempts] DEFAULT 0,
    [LastError] NVARCHAR(4000) NULL,
    CONSTRAINT [FK_ExternalEscalationOutcomeDeliveries_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExternalEscalationOutcomeDeliveries_Attempts] FOREIGN KEY ([ExternalEscalationAttemptId])
        REFERENCES [dbo].[ExternalEscalationAttempts] ([ExternalEscalationAttemptId]) ON DELETE CASCADE
);

GO

CREATE UNIQUE INDEX [IX_ExternalEscalationOutcomeDeliveries_Attempt_Outcome]
    ON [dbo].[ExternalEscalationOutcomeDeliveries] ([ExternalEscalationAttemptId], [OutcomeKey]);

GO

CREATE INDEX [IX_ExternalEscalationOutcomeDeliveries_Pending]
    ON [dbo].[ExternalEscalationOutcomeDeliveries] ([PublishedAt], [NextAttemptAt]);

GO
