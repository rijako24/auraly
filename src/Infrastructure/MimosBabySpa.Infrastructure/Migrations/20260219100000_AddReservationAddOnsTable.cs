using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Crea la tabla ReservationAddOns para asociar add-ons seleccionados a cada reserva.
    /// Usa IF NOT EXISTS para ser idempotente.
    /// </summary>
    [Migration("20260219100000_AddReservationAddOnsTable")]
    public partial class AddReservationAddOnsTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ReservationAddOns')
                BEGIN
                    CREATE TABLE [dbo].[ReservationAddOns] (
                        [ReservationAddOnId] UNIQUEIDENTIFIER NOT NULL,
                        [ReservationId]      UNIQUEIDENTIFIER NOT NULL,
                        [AddOnServiceId]     UNIQUEIDENTIFIER NOT NULL,
                        [PriceSnapshot]      DECIMAL(18,2)    NOT NULL,
                        CONSTRAINT [PK_ReservationAddOns] PRIMARY KEY ([ReservationAddOnId]),
                        CONSTRAINT [FK_ReservationAddOns_Reservations_ReservationId] 
                            FOREIGN KEY ([ReservationId]) REFERENCES [dbo].[Reservations]([ReservationId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ReservationAddOns_Services_AddOnServiceId] 
                            FOREIGN KEY ([AddOnServiceId]) REFERENCES [dbo].[Services]([ServiceId]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_ReservationAddOns_ReservationId] ON [dbo].[ReservationAddOns]([ReservationId]);
                    CREATE UNIQUE INDEX [IX_ReservationAddOns_ReservationId_AddOnServiceId] ON [dbo].[ReservationAddOns]([ReservationId], [AddOnServiceId]);
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ReservationAddOns')
                    DROP TABLE [dbo].[ReservationAddOns];
            ");
        }
    }
}
