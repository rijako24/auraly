CREATE TABLE [dbo].[FlowExecutionStates] (
    [FlowExecutionStateId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [UserIdentifier] NVARCHAR(100) NOT NULL,
    [AgentId] UNIQUEIDENTIFIER NOT NULL,
    [FlowDefinitionId] UNIQUEIDENTIFIER NOT NULL,
    [CurrentNodeId] NVARCHAR(100) NOT NULL DEFAULT N'start',
    [IsWaitingForUser] BIT NOT NULL DEFAULT 0,
    [Owner] NVARCHAR(20) NOT NULL DEFAULT N'Bot',
    [VariablesJson] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
    [FlagsJson] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
    [ActionResultsJson] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
    [TraceJson] NVARCHAR(MAX) NULL,
    [PreviousSessionJson] NVARCHAR(MAX) NULL,
    [ConversationHistoryJson] NVARCHAR(MAX) NULL,
    [ConsecutiveDegradedTurns] INT NOT NULL DEFAULT 0,
    [Version] INT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [SessionStartedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_FlowExecutionStates_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_FlowExecutionStates_FlowDefinitions] FOREIGN KEY ([FlowDefinitionId])
        REFERENCES [dbo].[FlowDefinitions] ([FlowDefinitionId])
        ON DELETE NO ACTION,
    CONSTRAINT [UQ_FlowExecutionStates_Session] UNIQUE ([BusinessId], [UserIdentifier], [AgentId])
);

GO

CREATE INDEX [IX_FlowExecutionStates_AgentId] ON [dbo].[FlowExecutionStates] ([AgentId]);

GO
