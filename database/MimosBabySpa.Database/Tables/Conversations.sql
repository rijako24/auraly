CREATE TABLE [dbo].[Conversations] (
    [ConversationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [UserNumber] NVARCHAR(50) NOT NULL,
    [LastMessage] NVARCHAR(1000) NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CustomerName] NVARCHAR(100) NULL,
    [CustomerEmail] NVARCHAR(200) NULL,
    [CurrentStageName] NVARCHAR(100) NULL,
    [Status] TINYINT NOT NULL DEFAULT 0,
    [OpenedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [LastActivityAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ClosedAt] DATETIME2 NULL,
    [CloseReason] NVARCHAR(50) NULL,
    CONSTRAINT [FK_Conversations_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_Conversations_BusinessId_UserNumber] ON [dbo].[Conversations] ([BusinessId], [UserNumber]);

GO

CREATE UNIQUE INDEX [UX_Conversations_OneActivePerCustomer]
ON [dbo].[Conversations] ([BusinessId], [UserNumber])
WHERE [Status] = 0;

GO
