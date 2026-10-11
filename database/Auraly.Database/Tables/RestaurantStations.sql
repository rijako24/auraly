CREATE TABLE [sales].[RestaurantStations]
(
    [StationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [SortOrder] INT NOT NULL CONSTRAINT [DF_RestaurantStations_SortOrder] DEFAULT (0),
    [Kind] NVARCHAR(16) NOT NULL,
    [TargetMinutes] SMALLINT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_RestaurantStations_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantStations] PRIMARY KEY ([StationId]),
    CONSTRAINT [UQ_RestaurantStations_Business_Id] UNIQUE ([BusinessId],[StationId]),
    CONSTRAINT [UQ_RestaurantStations_Business_Code] UNIQUE ([BusinessId],[Code]),
    CONSTRAINT [FK_RestaurantStations_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [CK_RestaurantStations_Code] CHECK (LEN(LTRIM(RTRIM([Code])))>0),
    CONSTRAINT [CK_RestaurantStations_Name] CHECK (LEN(LTRIM(RTRIM([Name])))>0),
    CONSTRAINT [CK_RestaurantStations_SortOrder] CHECK ([SortOrder]>=0),
    CONSTRAINT [CK_RestaurantStations_Kind] CHECK ([Kind] IN (N'Preparation',N'Pickup')),
    CONSTRAINT [CK_RestaurantStations_TargetMinutes] CHECK ([TargetMinutes] IS NULL OR [TargetMinutes]>0)
);
GO
CREATE INDEX [IX_RestaurantStations_Business_Active_Order]
    ON [sales].[RestaurantStations] ([BusinessId],[IsActive],[SortOrder],[Name]);
GO
