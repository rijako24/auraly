CREATE TABLE [dbo].[CampaignRecipients] (
    [CampaignRecipientId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [CampaignId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PhoneNormalized] NVARCHAR(50) NOT NULL,
    [CustomerName] NVARCHAR(160) NULL,
    [SourceLeadId] UNIQUEIDENTIFIER NULL,
    [SourceReservationId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(30) NOT NULL,
    [WhatsAppMessageId] NVARCHAR(160) NULL,
    [Error] NVARCHAR(1000) NULL,
    [VariablesJson] NVARCHAR(MAX) NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_CampaignRecipients_AttemptCount] DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CampaignRecipients_CreatedAt] DEFAULT GETUTCDATE(),
    [LastAttemptAtUtc] DATETIME2 NULL,
    [SentAt] DATETIME2 NULL,
    CONSTRAINT [FK_CampaignRecipients_Campaigns] FOREIGN KEY ([CampaignId])
        REFERENCES [dbo].[Campaigns] ([CampaignId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_CampaignRecipients_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_CampaignRecipients_Leads] FOREIGN KEY ([SourceLeadId])
        REFERENCES [dbo].[Leads] ([LeadId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_CampaignRecipients_Reservations] FOREIGN KEY ([SourceReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_CampaignRecipients_CampaignId_PhoneNormalized]
    ON [dbo].[CampaignRecipients] ([CampaignId], [PhoneNormalized]);

GO

CREATE INDEX [IX_CampaignRecipients_BusinessId_Status]
    ON [dbo].[CampaignRecipients] ([BusinessId], [Status]);

GO
