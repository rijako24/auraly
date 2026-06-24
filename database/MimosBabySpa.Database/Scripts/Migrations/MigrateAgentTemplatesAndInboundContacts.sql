IF OBJECT_ID(N'[dbo].[AgentTemplates]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AgentTemplates] (
        [AgentTemplateId]      UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_AgentTemplates] PRIMARY KEY DEFAULT NEWID(),
        [Key]                  NVARCHAR(100)    NOT NULL,
        [Name]                 NVARCHAR(200)    NOT NULL,
        [Kind]                 NVARCHAR(50)     NOT NULL,
        [Description]          NVARCHAR(500)    NULL,
        [SettingsJson]         NVARCHAR(MAX)    NULL,
        [SystemPromptMarkdown] NVARCHAR(MAX)    NULL,
        [IsSystemTemplate]     BIT              NOT NULL DEFAULT 1,
        [IsActive]             BIT              NOT NULL DEFAULT 1,
        [CreatedAt]            DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]            DATETIME2        NULL,
        CONSTRAINT [UQ_AgentTemplates_Key] UNIQUE ([Key])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentTemplates_Kind' AND object_id = OBJECT_ID(N'[dbo].[AgentTemplates]'))
    CREATE INDEX [IX_AgentTemplates_Kind] ON [dbo].[AgentTemplates] ([Kind]);

IF COL_LENGTH(N'[dbo].[Agents]', N'AgentTemplateId') IS NULL
    ALTER TABLE [dbo].[Agents] ADD [AgentTemplateId] UNIQUEIDENTIFIER NULL;

IF COL_LENGTH(N'[dbo].[Agents]', N'Kind') IS NULL
    ALTER TABLE [dbo].[Agents] ADD [Kind] NVARCHAR(50) NOT NULL CONSTRAINT [DF_Agents_Kind] DEFAULT N'customer';

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Agents_AgentTemplates')
    ALTER TABLE [dbo].[Agents] WITH CHECK ADD CONSTRAINT [FK_Agents_AgentTemplates] FOREIGN KEY ([AgentTemplateId]) REFERENCES [dbo].[AgentTemplates] ([AgentTemplateId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Agents_AgentTemplateId' AND object_id = OBJECT_ID(N'[dbo].[Agents]'))
    CREATE INDEX [IX_Agents_AgentTemplateId] ON [dbo].[Agents] ([AgentTemplateId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Agents_BusinessId_Kind' AND object_id = OBJECT_ID(N'[dbo].[Agents]'))
    CREATE INDEX [IX_Agents_BusinessId_Kind] ON [dbo].[Agents] ([BusinessId], [Kind]);

IF OBJECT_ID(N'[dbo].[BusinessInboundContacts]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BusinessInboundContacts] (
        [BusinessInboundContactId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_BusinessInboundContacts] PRIMARY KEY DEFAULT NEWID(),
        [BusinessId]              UNIQUEIDENTIFIER NOT NULL,
        [Type]                    NVARCHAR(50)     NOT NULL,
        [Key]                     NVARCHAR(100)    NOT NULL,
        [Name]                    NVARCHAR(200)    NOT NULL,
        [Role]                    NVARCHAR(100)    NOT NULL DEFAULT N'',
        [PhoneNumber]             NVARCHAR(50)     NOT NULL,
        [PhoneNormalized]         NVARCHAR(50)     NOT NULL,
        [InboundAgentId]          UNIQUEIDENTIFIER NOT NULL,
        [EmployeeId]              UNIQUEIDENTIFIER NULL,
        [CapabilitiesJson]        NVARCHAR(MAX)    NULL,
        [IsActive]                BIT              NOT NULL DEFAULT 1,
        [CreatedAt]               DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]               DATETIME2        NULL,
        CONSTRAINT [FK_BusinessInboundContacts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
        CONSTRAINT [FK_BusinessInboundContacts_InboundAgents] FOREIGN KEY ([InboundAgentId]) REFERENCES [dbo].[Agents] ([AgentId]),
        CONSTRAINT [FK_BusinessInboundContacts_Employees] FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees] ([EmployeeId])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BusinessInboundContacts_BusinessId_PhoneNormalized' AND object_id = OBJECT_ID(N'[dbo].[BusinessInboundContacts]'))
    CREATE UNIQUE INDEX [IX_BusinessInboundContacts_BusinessId_PhoneNormalized] ON [dbo].[BusinessInboundContacts] ([BusinessId], [PhoneNormalized]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BusinessInboundContacts_BusinessId_Type' AND object_id = OBJECT_ID(N'[dbo].[BusinessInboundContacts]'))
    CREATE INDEX [IX_BusinessInboundContacts_BusinessId_Type] ON [dbo].[BusinessInboundContacts] ([BusinessId], [Type]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BusinessInboundContacts_InboundAgentId' AND object_id = OBJECT_ID(N'[dbo].[BusinessInboundContacts]'))
    CREATE INDEX [IX_BusinessInboundContacts_InboundAgentId] ON [dbo].[BusinessInboundContacts] ([InboundAgentId]);

IF COL_LENGTH(N'[dbo].[ExternalEscalationAttempts]', N'BusinessInboundContactIdSnapshot') IS NULL
    ALTER TABLE [dbo].[ExternalEscalationAttempts] ADD [BusinessInboundContactIdSnapshot] UNIQUEIDENTIFIER NULL;

IF COL_LENGTH(N'[dbo].[ExternalEscalationAttempts]', N'ContactTypeSnapshot') IS NULL
    ALTER TABLE [dbo].[ExternalEscalationAttempts] ADD [ContactTypeSnapshot] NVARCHAR(50) NULL;

IF COL_LENGTH(N'[dbo].[ExternalEscalationAttempts]', N'PickupAddressSnapshot') IS NULL
    ALTER TABLE [dbo].[ExternalEscalationAttempts] ADD [PickupAddressSnapshot] NVARCHAR(500) NULL;
