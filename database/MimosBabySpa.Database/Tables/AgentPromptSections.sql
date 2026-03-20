CREATE TABLE [dbo].[AgentPromptSections] (
    [AgentPromptSectionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [AgentId] UNIQUEIDENTIFIER NOT NULL,
    [Key] NVARCHAR(100) NOT NULL,
    [Title] NVARCHAR(200) NOT NULL,
    [Content] NVARCHAR(MAX) NOT NULL,
    [InjectionPoint] NVARCHAR(50) NOT NULL DEFAULT N'before_instructions',
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_AgentPromptSections_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE CASCADE,
    CONSTRAINT [UQ_AgentPromptSections_AgentKey] UNIQUE ([AgentId], [Key])
);

GO

CREATE INDEX [IX_AgentPromptSections_AgentId] ON [dbo].[AgentPromptSections] ([AgentId]);

GO
