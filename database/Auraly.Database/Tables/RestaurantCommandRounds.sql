CREATE TABLE [sales].[RestaurantCommandRounds]
(
    [RoundId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [OperationId] UNIQUEIDENTIFIER NOT NULL,
    [ExpectedAccountVersion] BIGINT NOT NULL,
    [SelectedAdditionId] UNIQUEIDENTIFIER NULL,
    [SentByUserId] UNIQUEIDENTIFIER NOT NULL,
    [ConnectedUserId] UNIQUEIDENTIFIER NULL,
    [SentAt] DATETIMEOFFSET(7) NOT NULL,
    [ResultJson] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_RestaurantCommandRounds] PRIMARY KEY ([RoundId]),
    CONSTRAINT [UQ_RestaurantCommandRounds_Business_Id] UNIQUE ([BusinessId],[RoundId]),
    CONSTRAINT [UQ_RestaurantCommandRounds_Operation] UNIQUE ([BusinessId],[OperationId]),
    CONSTRAINT [FK_RestaurantCommandRounds_Account] FOREIGN KEY ([BusinessId],[AccountId])
        REFERENCES [sales].[RestaurantAccounts] ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantCommandRounds_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantCommandRounds_Sender] FOREIGN KEY ([SentByUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantCommandRounds_Connected] FOREIGN KEY ([ConnectedUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [CK_RestaurantCommandRounds_Version] CHECK ([ExpectedAccountVersion]>0)
);
GO
CREATE INDEX [IX_RestaurantCommandRounds_Account]
    ON [sales].[RestaurantCommandRounds] ([AccountId],[SentAt] DESC);
