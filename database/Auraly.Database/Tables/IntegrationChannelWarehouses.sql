CREATE TABLE [dbo].[IntegrationChannelWarehouses] (
    [IntegrationChannelWarehouseId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId]                    UNIQUEIDENTIFIER NOT NULL,
    [IntegrationConnectionId]       UNIQUEIDENTIFIER NOT NULL,
    [BusinessWhatsAppNumberId]      UNIQUEIDENTIFIER NOT NULL,
    [WarehouseCode]                 NVARCHAR(100) NOT NULL,
    [WarehouseName]                 NVARCHAR(200) NULL,
    [IsActive]                      BIT NOT NULL DEFAULT 1,
    [CreatedAt]                     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]                     DATETIME2 NULL,
    CONSTRAINT [FK_IntegrationChannelWarehouses_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_IntegrationChannelWarehouses_IntegrationConnections]
        FOREIGN KEY ([IntegrationConnectionId]) REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId]) ON DELETE CASCADE,
    CONSTRAINT [FK_IntegrationChannelWarehouses_BusinessWhatsAppNumbers]
        FOREIGN KEY ([BusinessWhatsAppNumberId]) REFERENCES [dbo].[BusinessWhatsAppNumbers] ([BusinessWhatsAppNumberId])
);

GO

CREATE UNIQUE INDEX [UX_IntegrationChannelWarehouses_Connection_Number]
    ON [dbo].[IntegrationChannelWarehouses] ([IntegrationConnectionId], [BusinessWhatsAppNumberId]);

GO

CREATE INDEX [IX_IntegrationChannelWarehouses_BusinessId]
    ON [dbo].[IntegrationChannelWarehouses] ([BusinessId]);
