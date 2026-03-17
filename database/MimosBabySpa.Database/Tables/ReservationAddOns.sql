CREATE TABLE [dbo].[ReservationAddOns] (
    [ReservationAddOnId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [ReservationId] UNIQUEIDENTIFIER NOT NULL,
    [AddOnServiceId] UNIQUEIDENTIFIER NOT NULL,
    [PriceSnapshot] DECIMAL(18, 2) NOT NULL,
    CONSTRAINT [FK_ReservationAddOns_Reservations] FOREIGN KEY ([ReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_ReservationAddOns_Services] FOREIGN KEY ([AddOnServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_ReservationAddOns_ReservationId_AddOnServiceId] ON [dbo].[ReservationAddOns] ([ReservationId], [AddOnServiceId]);

GO

CREATE INDEX [IX_ReservationAddOns_ReservationId] ON [dbo].[ReservationAddOns] ([ReservationId]);

GO

CREATE INDEX [IX_ReservationAddOns_AddOnServiceId] ON [dbo].[ReservationAddOns] ([AddOnServiceId]);

GO
