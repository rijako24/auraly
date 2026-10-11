CREATE TABLE [sales].[RestaurantRooms]
(
    [RoomId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [SortOrder] INT NOT NULL CONSTRAINT [DF_RestaurantRooms_SortOrder] DEFAULT (0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_RestaurantRooms_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [FloorPlanJson] NVARCHAR(MAX) NULL,
    [DraftFloorPlanJson] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_RestaurantRooms] PRIMARY KEY ([RoomId]),
    CONSTRAINT [UQ_RestaurantRooms_Business_Id] UNIQUE ([BusinessId],[RoomId]),
    CONSTRAINT [UQ_RestaurantRooms_Business_Name] UNIQUE ([BusinessId],[Name]),
    CONSTRAINT [FK_RestaurantRooms_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [CK_RestaurantRooms_Name] CHECK (LEN(LTRIM(RTRIM([Name])))>0),
    CONSTRAINT [CK_RestaurantRooms_SortOrder] CHECK ([SortOrder]>=0),
    CONSTRAINT [CK_RestaurantRooms_FloorPlanJson] CHECK
        ([FloorPlanJson] IS NULL OR ISJSON([FloorPlanJson])=1),
    CONSTRAINT [CK_RestaurantRooms_DraftFloorPlanJson] CHECK
        ([DraftFloorPlanJson] IS NULL OR ISJSON([DraftFloorPlanJson])=1)
);
GO
CREATE INDEX [IX_RestaurantRooms_Business_Active_Order]
    ON [sales].[RestaurantRooms] ([BusinessId],[IsActive],[SortOrder],[Name]);
GO
