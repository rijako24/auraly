CREATE TABLE [sales].[RestaurantCancellationRequests]
(
    [CancellationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [AdditionId] UNIQUEIDENTIFIER NOT NULL,
    [CardId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] DECIMAL(18,4) NOT NULL,
    [ExpectedAccountVersion] BIGINT NOT NULL,
    [ReasonCode] NVARCHAR(40) NOT NULL,
    [Explanation] NVARCHAR(500) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [PreparationState] NVARCHAR(24) NULL,
    [ConsumedComponentsJson] NVARCHAR(MAX) NULL,
    [RequestedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [RequestedAt] DATETIMEOFFSET(7) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NULL,
    [ConfirmedAt] DATETIMEOFFSET(7) NULL,
    [ResolvedByUserId] UNIQUEIDENTIFIER NULL,
    [ResolvedAt] DATETIMEOFFSET(7) NULL,
    [InventoryDocumentId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantCancellationRequests] PRIMARY KEY ([CancellationId]),
    CONSTRAINT [FK_RestaurantCancellationRequests_Account] FOREIGN KEY ([BusinessId],[AccountId])
        REFERENCES [sales].[RestaurantAccounts] ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantCancellationRequests_Addition] FOREIGN KEY ([AccountId],[AdditionId])
        REFERENCES [sales].[RestaurantAccountAdditions] ([AccountId],[AdditionId]),
    CONSTRAINT [FK_RestaurantCancellationRequests_Card] FOREIGN KEY ([CardId],[AccountId])
        REFERENCES [sales].[RestaurantCommandCards] ([CardId],[AccountId]),
    CONSTRAINT [FK_RestaurantCancellationRequests_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantCancellationRequests_RequestedBy] FOREIGN KEY ([RequestedByUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantCancellationRequests_ConfirmedBy] FOREIGN KEY ([ConfirmedByUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantCancellationRequests_ResolvedBy] FOREIGN KEY ([ResolvedByUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [CK_RestaurantCancellationRequests_Quantity] CHECK ([Quantity]>0),
    CONSTRAINT [CK_RestaurantCancellationRequests_Version] CHECK ([ExpectedAccountVersion]>0),
    CONSTRAINT [CK_RestaurantCancellationRequests_Status] CHECK ([Status] IN
        (N'Requested',N'Confirmed',N'Approved',N'Rejected')),
    CONSTRAINT [CK_RestaurantCancellationRequests_Preparation] CHECK (
        ([PreparationState] IS NULL AND [Status] IN (N'Requested',N'Rejected')) OR
        ([PreparationState] IN (N'NotPrepared',N'Prepared',N'Partial') AND [Status]<>N'Requested'))
);
GO
CREATE UNIQUE INDEX [UX_RestaurantCancellationRequests_Active_Addition]
    ON [sales].[RestaurantCancellationRequests] ([AdditionId])
    WHERE [Status] IN (N'Requested',N'Confirmed');
GO
CREATE INDEX [IX_RestaurantCancellationRequests_Card_Status]
    ON [sales].[RestaurantCancellationRequests] ([CardId],[Status]);
GO
CREATE INDEX [IX_RestaurantCancellationRequests_Account_Status]
    ON [sales].[RestaurantCancellationRequests] ([AccountId],[Status]);
GO
