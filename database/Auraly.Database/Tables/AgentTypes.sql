CREATE TABLE [dbo].[AgentTypes] (
    [AgentTypeId]   UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_AgentTypes] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Name]          NVARCHAR(100)    NOT NULL,
    [Description]   NVARCHAR(500)    NULL,
    [IsActive]      BIT              NOT NULL DEFAULT 1,
    [CreatedAt]     DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

GO

CREATE UNIQUE INDEX [IX_AgentTypes_Name] ON [dbo].[AgentTypes] ([Name]);

GO
