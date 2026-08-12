CREATE TABLE [dbo].[Campaigns] (
    [CampaignId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    [SourceType] NVARCHAR(30) NOT NULL,
    [FiltersJson] NVARCHAR(MAX) NULL,
    [TemplateName] NVARCHAR(120) NOT NULL,
    [LanguageCode] NVARCHAR(20) NOT NULL,
    [TemplateCategory] NVARCHAR(30) NOT NULL CONSTRAINT [DF_Campaigns_TemplateCategory] DEFAULT N'Marketing',
    [ParameterMappingJson] NVARCHAR(MAX) NULL,
    [ScheduledAtUtc] DATETIME2 NULL,
    [RecipientCount] INT NOT NULL CONSTRAINT [DF_Campaigns_RecipientCount] DEFAULT 0,
    [SentCount] INT NOT NULL CONSTRAINT [DF_Campaigns_SentCount] DEFAULT 0,
    [FailedCount] INT NOT NULL CONSTRAINT [DF_Campaigns_FailedCount] DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Campaigns_CreatedAt] DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Campaigns_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Campaigns_AppUsers] FOREIGN KEY ([CreatedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_Campaigns_BusinessId_CreatedAt]
    ON [dbo].[Campaigns] ([BusinessId], [CreatedAt]);

GO
