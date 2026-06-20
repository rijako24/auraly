IF OBJECT_ID(N'[dbo].[ExternalEscalationAttempts]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ExternalEscalationAttempts] (
        [ExternalEscalationAttemptId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_ExternalEscalationAttempts] PRIMARY KEY DEFAULT NEWID(),
        [BusinessId]              UNIQUEIDENTIFIER NOT NULL,
        [SourceAgentId]           UNIQUEIDENTIFIER NOT NULL,
        [EventName]               NVARCHAR(100)    NOT NULL,
        [TargetType]              NVARCHAR(50)     NOT NULL,
        [TargetId]                UNIQUEIDENTIFIER NOT NULL,
        [ContactKey]              NVARCHAR(100)    NOT NULL,
        [ContactNameSnapshot]     NVARCHAR(200)    NOT NULL,
        [ContactRoleSnapshot]     NVARCHAR(100)    NOT NULL,
        [ContactPhoneSnapshot]    NVARCHAR(50)     NOT NULL,
        [InboundAgentIdSnapshot]  UNIQUEIDENTIFIER NOT NULL,
        [AttemptCode]               NVARCHAR(50)     NOT NULL,
        [CustomPayloadJson]       NVARCHAR(MAX)    NULL,
        [WhatsAppMessageId]       NVARCHAR(128)    NULL,
        [Status]                  INT              NOT NULL,
        [EscalatedAt]               DATETIME2        NOT NULL,
        [ExpiresAt]               DATETIME2        NOT NULL,
        [AcceptedAt]              DATETIME2        NULL,
        [DeclinedAt]              DATETIME2        NULL,
        [TimedOutAt]              DATETIME2        NULL,
        [CancelledAt]             DATETIME2        NULL,
        CONSTRAINT [FK_ExternalEscalationAttempts_Businesses] FOREIGN KEY ([BusinessId])
            REFERENCES [dbo].[Businesses] ([BusinessId]),
        CONSTRAINT [FK_ExternalEscalationAttempts_SourceAgents] FOREIGN KEY ([SourceAgentId])
            REFERENCES [dbo].[Agents] ([AgentId]),
        CONSTRAINT [FK_ExternalEscalationAttempts_InboundAgents] FOREIGN KEY ([InboundAgentIdSnapshot])
            REFERENCES [dbo].[Agents] ([AgentId]),
        CONSTRAINT [CK_ExternalEscalationAttempts_Status] CHECK ([Status] IN (0, 1, 2, 3, 4))
    );

    CREATE INDEX [IX_ExternalEscalationAttempts_BusinessId] ON [dbo].[ExternalEscalationAttempts] ([BusinessId]);
    CREATE INDEX [IX_ExternalEscalationAttempts_Target] ON [dbo].[ExternalEscalationAttempts] ([BusinessId], [EventName], [TargetType], [TargetId]);
    CREATE INDEX [IX_ExternalEscalationAttempts_Contact_Status] ON [dbo].[ExternalEscalationAttempts] ([BusinessId], [ContactPhoneSnapshot], [Status]);
    CREATE INDEX [IX_ExternalEscalationAttempts_AttemptCode] ON [dbo].[ExternalEscalationAttempts] ([BusinessId], [AttemptCode], [ContactPhoneSnapshot]);
    CREATE INDEX [IX_ExternalEscalationAttempts_WhatsAppMessageId] ON [dbo].[ExternalEscalationAttempts] ([WhatsAppMessageId]);
END;

IF OBJECT_ID(N'[dbo].[ExternalEscalationAttempts]', N'U') IS NOT NULL
   AND COL_LENGTH(N'[dbo].[ExternalEscalationAttempts]', N'CustomPayloadJson') IS NULL
BEGIN
    ALTER TABLE [dbo].[ExternalEscalationAttempts]
        ADD [CustomPayloadJson] NVARCHAR(MAX) NULL;
END;
