CREATE TABLE [dbo].[Agents] (
    [AgentId]               UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_Agents] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [BusinessId]            UNIQUEIDENTIFIER NOT NULL,
    [AgentTypeId]           UNIQUEIDENTIFIER NOT NULL,
    [AgentTemplateId]       UNIQUEIDENTIFIER NULL,
    [BotType]               INT              NOT NULL CONSTRAINT [DF_Agents_BotType] DEFAULT 1,
    [Name]                  NVARCHAR(200)    NOT NULL,
    [Description]           NVARCHAR(500)    NULL,
    [Kind]                  NVARCHAR(50)     NOT NULL DEFAULT N'customer',
    [IsActive]              BIT              NOT NULL DEFAULT 1,
    [SettingsJson]          NVARCHAR(MAX)    NULL,
    [SystemPromptMarkdown]  NVARCHAR(MAX)    NULL,
    [Model]                 NVARCHAR(100)    NULL,
    [Temperature]           DECIMAL(3,2)     NULL,
    [MaxToolIterations]      INT              NULL,
    [CreatedAt]             DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]             DATETIME2        NULL,
    CONSTRAINT [FK_Agents_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_Agents_AgentTypes] FOREIGN KEY ([AgentTypeId])
        REFERENCES [dbo].[AgentTypes] ([AgentTypeId]),
    CONSTRAINT [FK_Agents_AgentTemplates] FOREIGN KEY ([AgentTemplateId])
        REFERENCES [dbo].[AgentTemplates] ([AgentTemplateId]),
    CONSTRAINT [CK_Agents_BotType] CHECK ([BotType] IN (1, 2, 3, 4)),
    CONSTRAINT [UQ_Agents_BusinessName] UNIQUE ([BusinessId], [Name])
);

GO

CREATE INDEX [IX_Agents_BusinessId] ON [dbo].[Agents] ([BusinessId]);

GO

CREATE INDEX [IX_Agents_AgentTemplateId] ON [dbo].[Agents] ([AgentTemplateId]);

GO

CREATE INDEX [IX_Agents_BusinessId_Kind] ON [dbo].[Agents] ([BusinessId], [Kind]);

GO
