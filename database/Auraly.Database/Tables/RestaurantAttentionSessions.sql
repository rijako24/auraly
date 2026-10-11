CREATE TABLE [sales].[RestaurantAttentionSessions]
(
    [AttentionSessionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [TableId] UNIQUEIDENTIFIER NOT NULL,
    [ConnectedUserId] UNIQUEIDENTIFIER NOT NULL,
    [AuthenticationSessionId] UNIQUEIDENTIFIER NOT NULL,
    [WaiterUserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] VARBINARY(32) NOT NULL,
    [Method] NVARCHAR(16) NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [EndedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_RestaurantAttentionSessions] PRIMARY KEY ([AttentionSessionId]),
    CONSTRAINT [UQ_RestaurantAttentionSessions_TokenHash] UNIQUE ([TokenHash]),
    CONSTRAINT [FK_RestaurantAttentionSessions_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantAttentionSessions_Table] FOREIGN KEY ([BusinessId],[TableId])
        REFERENCES [sales].[RestaurantTables] ([BusinessId],[TableId]),
    CONSTRAINT [FK_RestaurantAttentionSessions_ConnectedUser] FOREIGN KEY ([ConnectedUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantAttentionSessions_WaiterUser] FOREIGN KEY ([WaiterUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantAttentionSessions_AuthenticationSession] FOREIGN KEY ([AuthenticationSessionId])
        REFERENCES [dbo].[AuthenticationSessions] ([AuthenticationSessionId]),
    CONSTRAINT [CK_RestaurantAttentionSessions_Method] CHECK ([Method] IN (N'Primary',N'Secondary',N'Selection')),
    CONSTRAINT [CK_RestaurantAttentionSessions_End] CHECK ([EndedAt] IS NULL OR [EndedAt]>=[IssuedAt])
);
GO
CREATE INDEX [IX_RestaurantAttentionSessions_Scope]
    ON [sales].[RestaurantAttentionSessions] ([TenantId],[BusinessId],[AuthenticationSessionId],[EndedAt])
    INCLUDE ([TableId],[WaiterUserId]);
