CREATE TABLE [dbo].[OrderConnectionEvents] (
    [OrderConnectionEventId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,
    [ConnectionType] INT NOT NULL DEFAULT 1,
    [Provider] INT NOT NULL,
    [Capability] INT NOT NULL,
    [ExternalEventId] NVARCHAR(500) NULL,
    [Status] INT NOT NULL DEFAULT 0,
    [LastError] NVARCHAR(4000) NULL,
    [RequestJson] NVARCHAR(MAX) NULL,
    [ResponseJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_OrderConnectionEvents_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderConnectionEvents_Orders] FOREIGN KEY ([OrderId])
        REFERENCES [dbo].[Orders] ([OrderId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_OrderConnectionEvents_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_OrderConnectionEvents_ConnectionType] CHECK ([ConnectionType] IN (0, 1)),
    CONSTRAINT [CK_OrderConnectionEvents_Status] CHECK ([Status] IN (0, 1, 2))
);

GO

CREATE UNIQUE INDEX [IX_OrderConnectionEvents_OrderId_IntegrationConnectionId]
    ON [dbo].[OrderConnectionEvents] ([OrderId], [IntegrationConnectionId]);
GO
CREATE INDEX [IX_OrderConnectionEvents_BusinessId]
    ON [dbo].[OrderConnectionEvents] ([BusinessId]);
GO
