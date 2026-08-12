CREATE TABLE [dbo].[SalesZones] (
    [ZoneId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_SalesZones_IsActive] DEFAULT (1),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesZones] PRIMARY KEY ([ZoneId]),
    CONSTRAINT [FK_SalesZones_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_SalesZones_Business_Code] UNIQUE ([BusinessId], [Code])
);
GO

CREATE INDEX [IX_SalesZones_Business_State_Name]
    ON [dbo].[SalesZones] ([BusinessId], [IsActive], [Name]);
GO

CREATE TABLE [dbo].[SalesRoutes] (
    [RouteId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [ZoneId] UNIQUEIDENTIFIER NULL,
    [SellerId] UNIQUEIDENTIFIER NOT NULL,
    [Notes] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_SalesRoutes_IsActive] DEFAULT (1),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesRoutes] PRIMARY KEY ([RouteId]),
    CONSTRAINT [FK_SalesRoutes_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesRoutes_SalesZones] FOREIGN KEY ([ZoneId]) REFERENCES [dbo].[SalesZones] ([ZoneId]),
    CONSTRAINT [FK_SalesRoutes_CommerceSellers] FOREIGN KEY ([SellerId]) REFERENCES [dbo].[CommerceSellers] ([SellerId]),
    CONSTRAINT [UQ_SalesRoutes_Business_Code] UNIQUE ([BusinessId], [Code])
);
GO

CREATE INDEX [IX_SalesRoutes_Business_State_Seller]
    ON [dbo].[SalesRoutes] ([BusinessId], [IsActive], [SellerId])
    INCLUDE ([ZoneId], [Name], [UpdatedAt]);
GO

CREATE TABLE [dbo].[SalesRouteSchedules] (
    [RouteScheduleId] UNIQUEIDENTIFIER NOT NULL,
    [RouteId] UNIQUEIDENTIFIER NOT NULL,
    [DayOfWeek] TINYINT NOT NULL,
    [RunOrder] INT NOT NULL,
    [PlannedStartTime] TIME(0) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_SalesRouteSchedules_IsActive] DEFAULT (1),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesRouteSchedules] PRIMARY KEY ([RouteScheduleId]),
    CONSTRAINT [FK_SalesRouteSchedules_SalesRoutes] FOREIGN KEY ([RouteId]) REFERENCES [dbo].[SalesRoutes] ([RouteId]),
    CONSTRAINT [CK_SalesRouteSchedules_DayOfWeek] CHECK ([DayOfWeek] BETWEEN 1 AND 7),
    CONSTRAINT [CK_SalesRouteSchedules_RunOrder] CHECK ([RunOrder] > 0)
);
GO

CREATE UNIQUE INDEX [UX_SalesRouteSchedules_Route_Day_Active]
    ON [dbo].[SalesRouteSchedules] ([RouteId], [DayOfWeek])
    WHERE [IsActive] = 1;
GO

CREATE INDEX [IX_SalesRouteSchedules_Day_Order]
    ON [dbo].[SalesRouteSchedules] ([DayOfWeek], [RunOrder], [RouteId])
    WHERE [IsActive] = 1;
GO

CREATE TABLE [dbo].[SalesRouteStops] (
    [RouteStopId] UNIQUEIDENTIFIER NOT NULL,
    [RouteId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [PartySiteId] UNIQUEIDENTIFIER NOT NULL,
    [Sequence] INT NOT NULL,
    [VisitNote] NVARCHAR(300) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_SalesRouteStops_IsActive] DEFAULT (1),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RemovedBy] UNIQUEIDENTIFIER NULL,
    [RemovedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SalesRouteStops] PRIMARY KEY ([RouteStopId]),
    CONSTRAINT [FK_SalesRouteStops_SalesRoutes] FOREIGN KEY ([RouteId]) REFERENCES [dbo].[SalesRoutes] ([RouteId]),
    CONSTRAINT [FK_SalesRouteStops_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]),
    CONSTRAINT [FK_SalesRouteStops_PartySites] FOREIGN KEY ([PartySiteId]) REFERENCES [dbo].[PartySites] ([PartySiteId]),
    CONSTRAINT [CK_SalesRouteStops_Sequence] CHECK ([Sequence] > 0)
);
GO

CREATE UNIQUE INDEX [UX_SalesRouteStops_Route_Site_Active]
    ON [dbo].[SalesRouteStops] ([RouteId], [PartySiteId])
    WHERE [IsActive] = 1;
GO

CREATE UNIQUE INDEX [UX_SalesRouteStops_Route_Sequence_Active]
    ON [dbo].[SalesRouteStops] ([RouteId], [Sequence])
    WHERE [IsActive] = 1;
GO

CREATE INDEX [IX_SalesRouteStops_Site_Active]
    ON [dbo].[SalesRouteStops] ([PartySiteId], [RouteId])
    INCLUDE ([CustomerId], [Sequence])
    WHERE [IsActive] = 1;
GO
