CREATE TABLE [sales].[RestaurantCommandLines]
(
    [CardId] UNIQUEIDENTIFIER NOT NULL,
    [AdditionId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] DECIMAL(18,4) NOT NULL,
    [ProductName] NVARCHAR(250) NOT NULL,
    [PreparationNote] NVARCHAR(500) NULL,
    [AddedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AddedByName] NVARCHAR(201) NOT NULL,
    [AddedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_RestaurantCommandLines] PRIMARY KEY ([CardId],[AdditionId]),
    CONSTRAINT [UQ_RestaurantCommandLines_Addition] UNIQUE ([AdditionId]),
    CONSTRAINT [FK_RestaurantCommandLines_Card] FOREIGN KEY ([CardId],[AccountId])
        REFERENCES [sales].[RestaurantCommandCards] ([CardId],[AccountId]),
    CONSTRAINT [FK_RestaurantCommandLines_Addition] FOREIGN KEY ([AccountId],[AdditionId])
        REFERENCES [sales].[RestaurantAccountAdditions] ([AccountId],[AdditionId]),
    CONSTRAINT [CK_RestaurantCommandLines_Quantity] CHECK ([Quantity]>0)
);
