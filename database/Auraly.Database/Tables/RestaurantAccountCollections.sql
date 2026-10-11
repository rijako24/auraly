CREATE TABLE [sales].[RestaurantAccountCollections]
(
    [CollectionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [CashierUserId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(128) NOT NULL,
    [RequestHash] BINARY(32) NOT NULL,
    [PaymentsJson] NVARCHAR(MAX) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL CONSTRAINT [DF_RestaurantAccountCollections_Status] DEFAULT N'Captured',
    [CapturedAt] DATETIMEOFFSET(7) NOT NULL,
    [ReversedAt] DATETIMEOFFSET(7) NULL,
    [ReversedByUserId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_RestaurantAccountCollections] PRIMARY KEY ([CollectionId]),
    CONSTRAINT [UQ_RestaurantAccountCollections_Business_Key] UNIQUE ([BusinessId],[IdempotencyKey]),
    CONSTRAINT [UQ_RestaurantAccountCollections_Account_Id] UNIQUE ([AccountId],[CollectionId]),
    CONSTRAINT [FK_RestaurantAccountCollections_Account] FOREIGN KEY ([BusinessId],[AccountId]) REFERENCES [sales].[RestaurantAccounts] ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantAccountCollections_Business] FOREIGN KEY ([BusinessId],[TenantId]) REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantAccountCollections_Session] FOREIGN KEY ([WorkSessionId],[BusinessId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId],[BusinessId]),
    CONSTRAINT [FK_RestaurantAccountCollections_Cashier] FOREIGN KEY ([CashierUserId],[TenantId]) REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantAccountCollections_Reverser] FOREIGN KEY ([ReversedByUserId],[TenantId]) REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [CK_RestaurantAccountCollections_Amount] CHECK ([Amount]>0),
    CONSTRAINT [CK_RestaurantAccountCollections_Status] CHECK
        (([Status]=N'Captured' AND [ReversedAt] IS NULL AND [ReversedByUserId] IS NULL)
         OR ([Status]=N'Reversed' AND [ReversedAt] IS NOT NULL AND [ReversedByUserId] IS NOT NULL))
);
GO
CREATE INDEX [IX_RestaurantAccountCollections_Account]
    ON [sales].[RestaurantAccountCollections] ([AccountId],[CapturedAt])
    INCLUDE ([Status],[Amount],[WorkSessionId]);
GO
CREATE INDEX [IX_RestaurantAccountCollections_Session_Open]
    ON [sales].[RestaurantAccountCollections] ([WorkSessionId],[AccountId])
    WHERE [Status]=N'Captured';
GO
CREATE TABLE [sales].[RestaurantCollectionAllocations]
(
    [CollectionId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [AdditionId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] DECIMAL(18,4) NOT NULL,
    CONSTRAINT [PK_RestaurantCollectionAllocations] PRIMARY KEY ([CollectionId],[AdditionId]),
    CONSTRAINT [FK_RestaurantCollectionAllocations_Collection] FOREIGN KEY ([AccountId],[CollectionId]) REFERENCES [sales].[RestaurantAccountCollections] ([AccountId],[CollectionId]),
    CONSTRAINT [FK_RestaurantCollectionAllocations_Addition] FOREIGN KEY ([AccountId],[AdditionId]) REFERENCES [sales].[RestaurantAccountAdditions] ([AccountId],[AdditionId]),
    CONSTRAINT [CK_RestaurantCollectionAllocations_Quantity] CHECK ([Quantity]>0)
);
GO
CREATE INDEX [IX_RestaurantCollectionAllocations_Account_Addition]
    ON [sales].[RestaurantCollectionAllocations] ([AccountId],[AdditionId]);
