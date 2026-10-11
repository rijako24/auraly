CREATE TABLE [sales].[RestaurantCommandCards]
(
    [CardId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [RoundId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [StationId] UNIQUEIDENTIFIER NOT NULL,
    [ConfigurationId] UNIQUEIDENTIFIER NOT NULL,
    [StationName] NVARCHAR(100) NOT NULL,
    [TableCode] NVARCHAR(32) NOT NULL,
    [State] NVARCHAR(20) NOT NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [StartedAt] DATETIMEOFFSET(7) NULL,
    [ReadyAt] DATETIMEOFFSET(7) NULL,
    [DeliveredAt] DATETIMEOFFSET(7) NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantCommandCards] PRIMARY KEY ([CardId]),
    CONSTRAINT [UQ_RestaurantCommandCards_Account_Id] UNIQUE ([CardId],[AccountId]),
    CONSTRAINT [UQ_RestaurantCommandCards_Round_Station] UNIQUE ([RoundId],[StationId]),
    CONSTRAINT [FK_RestaurantCommandCards_Round] FOREIGN KEY ([BusinessId],[RoundId])
        REFERENCES [sales].[RestaurantCommandRounds] ([BusinessId],[RoundId]),
    CONSTRAINT [FK_RestaurantCommandCards_Account] FOREIGN KEY ([BusinessId],[AccountId])
        REFERENCES [sales].[RestaurantAccounts] ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantCommandCards_Station] FOREIGN KEY ([BusinessId],[StationId])
        REFERENCES [sales].[RestaurantStations] ([BusinessId],[StationId]),
    CONSTRAINT [FK_RestaurantCommandCards_Configuration] FOREIGN KEY ([BusinessId],[StationId],[ConfigurationId])
        REFERENCES [sales].[RestaurantCommandConfigurations] ([BusinessId],[StationId],[ConfigurationId]),
    CONSTRAINT [FK_RestaurantCommandCards_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [CK_RestaurantCommandCards_State] CHECK ([State] IN
        (N'Pending',N'Preparing',N'Ready',N'ToDeliver',N'Delivered',N'Cancelled'))
);
GO
CREATE INDEX [IX_RestaurantCommandCards_Station_State]
    ON [sales].[RestaurantCommandCards] ([BusinessId],[StationId],[State],[ReceivedAt] DESC)
    INCLUDE ([TableCode],[StationName],[RoundId],[ConfigurationId]);
GO
CREATE INDEX [IX_RestaurantCommandCards_Open_Board]
    ON [sales].[RestaurantCommandCards] ([BusinessId],[ReceivedAt],[CardId])
    INCLUDE ([TenantId],[StationId],[StationName],[TableCode],[State])
    WHERE [State]<>N'Delivered';
GO
CREATE INDEX [IX_RestaurantCommandCards_Open_Station_Board]
    ON [sales].[RestaurantCommandCards] ([BusinessId],[StationId],[ReceivedAt],[CardId])
    INCLUDE ([TenantId],[StationName],[TableCode],[State])
    WHERE [State]<>N'Delivered';
GO
CREATE INDEX [IX_RestaurantCommandCards_Open_Table_Board]
    ON [sales].[RestaurantCommandCards] ([BusinessId],[TableCode],[ReceivedAt],[CardId])
    INCLUDE ([TenantId],[StationId],[StationName],[State])
    WHERE [State]<>N'Delivered';
