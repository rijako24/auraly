CREATE TABLE [sales].[RestaurantTables]
(
    [TableId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [RoomId] UNIQUEIDENTIFIER NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(100) NULL,
    [Capacity] SMALLINT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_RestaurantTables_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantTables] PRIMARY KEY ([TableId]),
    CONSTRAINT [UQ_RestaurantTables_Business_Id] UNIQUE ([BusinessId],[TableId]),
    CONSTRAINT [UQ_RestaurantTables_Business_Code] UNIQUE ([BusinessId],[Code]),
    CONSTRAINT [FK_RestaurantTables_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantTables_Room] FOREIGN KEY ([BusinessId],[RoomId])
        REFERENCES [sales].[RestaurantRooms] ([BusinessId],[RoomId]),
    CONSTRAINT [CK_RestaurantTables_Code] CHECK (LEN(LTRIM(RTRIM([Code])))>0),
    CONSTRAINT [CK_RestaurantTables_Capacity] CHECK ([Capacity] IS NULL OR [Capacity]>0)
);
GO
CREATE INDEX [IX_RestaurantTables_Business_Active_Room]
    ON [sales].[RestaurantTables] ([BusinessId],[IsActive],[RoomId],[Code]);
GO
