CREATE TABLE [dbo].[SalesDrafts] (
    [SalesDraftId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [SellerId] UNIQUEIDENTIFIER NULL,
    [SourceOrderId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [Name] NVARCHAR(120) NULL,
    [Reference] NVARCHAR(120) NULL,
    [Observation] NVARCHAR(500) NULL,
    [Version] BIGINT NOT NULL CONSTRAINT [DF_SalesDrafts_Version] DEFAULT 1,
    [CreatedAt] DATETIMEOFFSET NOT NULL,
    [UpdatedAt] DATETIMEOFFSET NOT NULL,
    [SavedAt] DATETIMEOFFSET NULL,
    [ConsumedAt] DATETIMEOFFSET NULL,
    [DeletedAt] DATETIMEOFFSET NULL,
    CONSTRAINT [PK_SalesDrafts] PRIMARY KEY ([SalesDraftId]),
    CONSTRAINT [FK_SalesDrafts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SalesDrafts_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_SalesDrafts_WorkSessions] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions] ([WorkSessionId]),
    CONSTRAINT [FK_SalesDrafts_AppUsers] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_SalesDrafts_Orders] FOREIGN KEY ([SourceOrderId]) REFERENCES [dbo].[Orders] ([OrderId]),
    CONSTRAINT [CK_SalesDrafts_Status] CHECK ([Status] IN (N'Active', N'Temporary', N'Issuing', N'Consumed', N'Deleted')),
    CONSTRAINT [CK_SalesDrafts_Version] CHECK ([Version] > 0)
);
GO

CREATE UNIQUE INDEX [UX_SalesDrafts_ActiveWorkSession]
    ON [dbo].[SalesDrafts] ([WorkSessionId])
    WHERE [Status] = N'Active';
GO

CREATE UNIQUE INDEX [UX_SalesDrafts_SourceOrder]
    ON [dbo].[SalesDrafts] ([SourceOrderId])
    WHERE [SourceOrderId] IS NOT NULL;
GO

CREATE INDEX [IX_SalesDrafts_Business_Status_Updated]
    ON [dbo].[SalesDrafts] ([BusinessId], [Status], [UpdatedAt] DESC);
GO