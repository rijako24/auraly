CREATE TABLE [dbo].[Conversations] (
    [ConversationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [UserNumber] NVARCHAR(50) NOT NULL,
    [LastMessage] NVARCHAR(1000) NULL,
    [LastIntent] NVARCHAR(50) NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CustomerName] NVARCHAR(100) NULL,
    [BabyAge] INT NULL,
    [RecommendedPlan] NVARCHAR(100) NULL
);

GO

CREATE INDEX [IX_Conversations_UserNumber] ON [dbo].[Conversations] ([UserNumber]);

GO
