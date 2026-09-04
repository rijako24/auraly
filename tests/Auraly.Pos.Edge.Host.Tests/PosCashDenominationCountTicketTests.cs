using System.Text;
using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosCashDenominationCountTicketTests
{
    [Fact]
    public void Denomination_count_prints_each_quantity_subtotal_and_total()
    {
        var ticket = new PosCashDenominationCountTicket(
            "Sede principal",
            "Cajero Uno",
            new DateTimeOffset(2026, 9, 3, 15, 30, 0, TimeSpan.FromHours(-5)),
            [
                new("Billete de 50.000", 50_000, 2, 100_000),
                new("Moneda de 1.000", 1_000, 3, 3_000),
            ],
            103_000);
        var workstation = new PosWorkstationIdentity(
            "02", "Sede principal", "Bodega", "Cajero Uno", "Empresa Prueba", null);

        PosCashDenominationCountTicketPrinter.Validate(ticket);
        var raw = Encoding.ASCII.GetString(
            PosCashDenominationCountTicketPrinter.RenderRaw(ticket, workstation, 80));
        var html = PosCashDenominationCountTicketPrinter.RenderHtml(ticket, workstation, 80);

        Assert.Contains("Conteo de efectivo", raw);
        Assert.Contains("Billete de 50.000", raw);
        Assert.Contains("2 x", raw);
        Assert.Contains("Total", raw);
        Assert.Contains("Empresa Prueba", html);
        Assert.Contains("Billete de 50.000", html);
        Assert.Contains("103.000", html);
        Assert.DoesNotContain("text-transform:uppercase", html);
        Assert.Contains("text-align:center", html);
    }

    [Fact]
    public void Denomination_count_rejects_a_total_that_does_not_match_its_rows()
    {
        var ticket = new PosCashDenominationCountTicket(
            "Sede", "Cajero", DateTimeOffset.UtcNow,
            [new("Billete", 20_000, 2, 40_000)], 39_000);

        Assert.Throws<ArgumentException>(() =>
            PosCashDenominationCountTicketPrinter.Validate(ticket));
    }
}
