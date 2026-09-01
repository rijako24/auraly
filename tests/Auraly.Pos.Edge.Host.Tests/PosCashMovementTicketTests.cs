using System.Text;
using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosCashMovementTicketTests
{
    [Theory]
    [InlineData("In", "ENTRADA DE DINERO")]
    [InlineData("Out", "SALIDA DE DINERO")]
    public void Ticket_contains_professional_movement_details_and_signature(
        string direction,
        string expectedTitle)
    {
        var ticket = new PosCashMovementTicket(
            Guid.Parse("8b10993e-8a86-4df3-913d-3f4341be3b8f"),
            direction,
            "Base inicial",
            125000m,
            new DateTimeOffset(2026, 8, 31, 14, 30, 0, TimeSpan.FromHours(-5)),
            "REC-45",
            "Entregado por administración",
            "Carol Cairo");
        var workstation = new PosWorkstationIdentity(
            "02", "Sede principal", "Bodega que no debe imprimirse", "Carol Cairo", "Auraly", null);

        var raw = Encoding.ASCII.GetString(
            PosCashMovementTicketPrinter.RenderRaw(ticket, workstation, 80));
        var html = PosCashMovementTicketPrinter.RenderHtml(ticket, workstation, 80);

        Assert.Contains(expectedTitle, raw);
        Assert.Contains("MOTIVO: Base inicial", raw);
        Assert.Contains("FIRMA:", raw);
        Assert.Contains("Carol Cairo", raw);
        Assert.Contains(expectedTitle, html);
        Assert.Contains("Entregado por administraci", html);
        Assert.Contains("class=\"signature\"", html);
        Assert.Contains("@page{size:80mm", html);
        Assert.DoesNotContain("<strong>Bodega:</strong>", html);
        Assert.DoesNotContain("Bodega que no debe imprimirse", html);
        Assert.DoesNotContain("Bodega que no debe imprimirse", raw);
        Assert.True(html.IndexOf("class=\"amount\"", StringComparison.Ordinal) <
                    html.IndexOf("class=\"signature\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Responsable:", StringComparison.Ordinal) <
                    html.IndexOf("class=\"amount\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Optional_reference_and_notes_are_omitted_from_the_ticket()
    {
        var ticket = new PosCashMovementTicket(
            Guid.NewGuid(), "In", "Base inicial", 10000m, DateTimeOffset.UtcNow,
            null, null, "Cajero");
        var workstation = new PosWorkstationIdentity(
            "02", "Bodega", "Sede", "Cajero", "Empresa", null);

        var raw = Encoding.ASCII.GetString(
            PosCashMovementTicketPrinter.RenderRaw(ticket, workstation, 58));
        var html = PosCashMovementTicketPrinter.RenderHtml(ticket, workstation, 58);

        Assert.DoesNotContain("REFERENCIA", raw);
        Assert.DoesNotContain("OBSERVACION", raw);
        Assert.DoesNotContain("Referencia", html);
        Assert.DoesNotContain("Observación", html);
        Assert.Contains("FIRMA:", raw);
        Assert.Contains("@page{size:58mm", html);
    }
}
