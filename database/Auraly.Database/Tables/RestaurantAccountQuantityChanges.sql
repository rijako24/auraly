CREATE TABLE [sales].[RestaurantAccountQuantityChanges]
(
    [ChangeId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [AdditionId] UNIQUEIDENTIFIER NOT NULL,
    [PreviousQuantity] DECIMAL(18,4) NOT NULL,
    [NewQuantity] DECIMAL(18,4) NOT NULL,
    [ExpectedVersion] BIGINT NOT NULL,
    [AccountVersionAfter] BIGINT NOT NULL,
    [ActorUserId] UNIQUEIDENTIFIER NOT NULL,
    [ChangedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_RestaurantAccountQuantityChanges] PRIMARY KEY ([ChangeId]),
    CONSTRAINT [FK_RestaurantAccountQuantityChanges_Account] FOREIGN KEY ([BusinessId],[AccountId])
        REFERENCES [sales].[RestaurantAccounts] ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantAccountQuantityChanges_Addition] FOREIGN KEY ([AccountId],[AdditionId])
        REFERENCES [sales].[RestaurantAccountAdditions] ([AccountId],[AdditionId]),
    CONSTRAINT [FK_RestaurantAccountQuantityChanges_User] FOREIGN KEY ([ActorUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantAccountQuantityChanges_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [CK_RestaurantAccountQuantityChanges_Quantities] CHECK
        ([PreviousQuantity]>0 AND [NewQuantity]>=0 AND
         [NewQuantity]<>[PreviousQuantity]),
    CONSTRAINT [CK_RestaurantAccountQuantityChanges_Versions] CHECK
        ([ExpectedVersion]>0 AND [AccountVersionAfter]=[ExpectedVersion]+1)
);
GO
CREATE INDEX [IX_RestaurantAccountQuantityChanges_Account_Changed]
    ON [sales].[RestaurantAccountQuantityChanges] ([AccountId],[ChangedAt],[ChangeId]);
GO
