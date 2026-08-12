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
    CONSTRAINT [FK_BusinessInboundContacts_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_BusinessInboundContacts_InboundAgents] FOREIGN KEY ([InboundAgentId])
        REFERENCES [dbo].[Agents] ([AgentId]),
    CONSTRAINT [FK_BusinessInboundContacts_Employees] FOREIGN KEY ([EmployeeId])
        REFERENCES [dbo].[Employees] ([EmployeeId])
);

GO

CREATE UNIQUE INDEX [IX_BusinessInboundContacts_BusinessId_PhoneNormalized] ON [dbo].[BusinessInboundContacts] ([BusinessId], [PhoneNormalized]);

GO

CREATE INDEX [IX_BusinessInboundContacts_BusinessId_Type] ON [dbo].[BusinessInboundContacts] ([BusinessId], [Type]);

GO

CREATE INDEX [IX_BusinessInboundContacts_InboundAgentId] ON [dbo].[BusinessInboundContacts] ([InboundAgentId]);

GO
