CREATE TABLE [dbo].[ReservationIntegrationEvents] (
    [ReservationIntegrationEventId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ReservationId] UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,
    [Provider] INT NOT NULL,
    [Capability] INT NOT NULL,
    [ExternalEventId] NVARCHAR(500) NULL,
    [Status] INT NOT NULL DEFAULT 0,
    [LastError] NVARCHAR(4000) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_ReservationIntegrationEvents_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_ReservationIntegrationEvents_Reservations] FOREIGN KEY ([ReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_ReservationIntegrationEvents_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_ReservationIntegrationEvents_Provider] CHECK ([Provider] IN (0, 1)),
    CONSTRAINT [CK_ReservationIntegrationEvents_Capability] CHECK ([Capability] IN (0, 1)),
    CONSTRAINT [CK_ReservationIntegrationEvents_Status] CHECK ([Status] IN (0, 1, 2))
);

GO

CREATE UNIQUE INDEX [IX_ReservationIntegrationEvents_ReservationId_IntegrationConnectionId]
    ON [dbo].[ReservationIntegrationEvents] ([ReservationId], [IntegrationConnectionId]);

GO

CREATE INDEX [IX_ReservationIntegrationEvents_BusinessId]
    ON [dbo].[ReservationIntegrationEvents] ([BusinessId]);

GO
