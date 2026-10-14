CREATE TABLE [sales].[RestaurantTipSettlements]
(
    [SettlementId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [DistributionBasis] NVARCHAR(24) NOT NULL,
    [AgreementReference] NVARCHAR(300) NULL,
    [PaymentMethodCode] NVARCHAR(16) NOT NULL,
    [BankAccountId] UNIQUEIDENTIFIER NULL,
    [PaymentReference] NVARCHAR(160) NULL,
    [RequestHash] BINARY(32) NOT NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_RestaurantTipSettlements] PRIMARY KEY ([SettlementId]),
    CONSTRAINT [FK_RestaurantTipSettlements_Business] FOREIGN KEY ([BusinessId],[TenantId]) REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantTipSettlements_User] FOREIGN KEY ([CreatedByUserId],[TenantId]) REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantTipSettlements_Bank] FOREIGN KEY ([BankAccountId]) REFERENCES [accounting].[BankAccounts] ([BankAccountId]),
    CONSTRAINT [CK_RestaurantTipSettlements_Basis] CHECK
        (([DistributionBasis]=N'EqualByLaw' AND [AgreementReference] IS NULL)
         OR ([DistributionBasis]=N'WorkerAgreement' AND [AgreementReference] IS NOT NULL)),
    CONSTRAINT [CK_RestaurantTipSettlements_Payment] CHECK
        (([PaymentMethodCode]=N'Cash' AND [BankAccountId] IS NULL)
         OR ([PaymentMethodCode]=N'Transfer' AND [BankAccountId] IS NOT NULL AND [PaymentReference] IS NOT NULL)),
    CONSTRAINT [CK_RestaurantTipSettlements_Amount] CHECK ([TotalAmount]>0)
);
GO
CREATE INDEX [IX_RestaurantTipSettlements_Business]
    ON [sales].[RestaurantTipSettlements] ([BusinessId],[CreatedAt] DESC)
    INCLUDE ([TotalAmount]);
GO
CREATE TABLE [sales].[RestaurantTipSettlementCollections]
(
    [CollectionId] UNIQUEIDENTIFIER NOT NULL,
    [SettlementId] UNIQUEIDENTIFIER NOT NULL,
    [TipAmount] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_RestaurantTipSettlementCollections] PRIMARY KEY ([CollectionId]),
    CONSTRAINT [FK_RestaurantTipSettlementCollections_Collection] FOREIGN KEY ([CollectionId]) REFERENCES [sales].[RestaurantAccountCollections] ([CollectionId]),
    CONSTRAINT [FK_RestaurantTipSettlementCollections_Settlement] FOREIGN KEY ([SettlementId]) REFERENCES [sales].[RestaurantTipSettlements] ([SettlementId]),
    CONSTRAINT [CK_RestaurantTipSettlementCollections_Amount] CHECK ([TipAmount]>0)
);
GO
CREATE INDEX [IX_RestaurantTipSettlementCollections_Settlement]
    ON [sales].[RestaurantTipSettlementCollections] ([SettlementId]);
GO
CREATE TABLE [sales].[RestaurantTipSettlementRecipients]
(
    [SettlementId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_RestaurantTipSettlementRecipients] PRIMARY KEY ([SettlementId],[PartyId]),
    CONSTRAINT [FK_RestaurantTipSettlementRecipients_Settlement] FOREIGN KEY ([SettlementId]) REFERENCES [sales].[RestaurantTipSettlements] ([SettlementId]),
    CONSTRAINT [FK_RestaurantTipSettlementRecipients_Party] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId]),
    CONSTRAINT [CK_RestaurantTipSettlementRecipients_Amount] CHECK ([Amount]>0)
);
