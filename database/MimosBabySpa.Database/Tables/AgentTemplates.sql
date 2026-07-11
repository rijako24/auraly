CREATE TABLE [dbo].[AgentTemplates] (
    [AgentTemplateId]      UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_AgentTemplates] PRIMARY KEY DEFAULT NEWID(),
    [Key]                  NVARCHAR(100)    NOT NULL,
    [Name]                 NVARCHAR(200)    NOT NULL,
    [Kind]                 NVARCHAR(50)     NOT NULL,
    [Description]          NVARCHAR(500)    NULL,
    [SettingsJson]         NVARCHAR(MAX)    NULL,
    [IsSystemTemplate]     BIT              NOT NULL DEFAULT 1,
    [IsActive]             BIT              NOT NULL DEFAULT 1,
    [CreatedAt]            DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]            DATETIME2        NULL,
    CONSTRAINT [UQ_AgentTemplates_Key] UNIQUE ([Key])
);

GO

CREATE INDEX [IX_AgentTemplates_Kind] ON [dbo].[AgentTemplates] ([Kind]);

GO
