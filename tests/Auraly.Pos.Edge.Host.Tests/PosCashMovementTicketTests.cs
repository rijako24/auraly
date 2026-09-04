using System.Text;
using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosCashMovementTicketTests
{
    [Theory]
    [InlineData("In", "Entrada de dinero")]
    [InlineData("Out", "Salida de dinero")]
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

        var rawBytes = PosCashMovementTicketPrinter.RenderRaw(ticket, workstation, 80);
        var raw = Encoding.ASCII.GetString(rawBytes);
        var html = PosCashMovementTicketPrinter.RenderHtml(ticket, workstation, 80);

        Assert.Contains(expectedTitle, raw);
        Assert.Contains("Motivo: Base inicial", raw);
        Assert.Contains("Firma:", raw);
        Assert.Contains("\n\n\nFirma:", raw);
        Assert.DoesNotContain(ticket.DocumentId.ToString("D"), raw);
        Assert.Contains("Carol Cairo", raw);
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x61, 0x01 }, rawBytes.Take(5).ToArray());
        Assert.Contains("\u001bE\u0001Valor", raw);
        Assert.Contains(expectedTitle, html);
        var expectedTemplate = direction == "In" ? "cash-entry" : "cash-exit";
        Assert.Contains($"data-auraly-report=\"{expectedTemplate}\"", html);
        Assert.Contains("data-auraly-report-version=\"1\"", html);
        Assert.Contains("font:700 12px/1.4 Arial", html);
        Assert.Contains("www.auralyapp.co", html);
        Assert.Contains("Entregado por administraci", html);
        Assert.Contains("class=\"signature\"", html);
        Assert.DoesNotContain(ticket.DocumentId.ToString("D"), html);
        Assert.Contains("@page{size:80mm", html);
        Assert.Contains("body{width:80mm", html);
        Assert.Contains("padding:5mm 3mm 2mm 2mm", html);
        Assert.DoesNotContain("body{text-transform:uppercase", html);
        Assert.Contains("text-transform:uppercase", html);
        Assert.Contains("text-align:center", html);
        Assert.Contains("Sede: Sede principal - Bodega que no debe imprimirse", html);
        Assert.Contains("Sede: Sede principal - Bodega que no", raw);
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

        Assert.DoesNotContain("Referencia", raw);
        Assert.DoesNotContain("Observacion", raw);
        Assert.DoesNotContain("Referencia", html);
        Assert.DoesNotContain("Observación", html);
        Assert.Contains("Firma:", raw);
        Assert.Contains("@page{size:58mm", html);
        Assert.Contains("body{width:58mm", html);
    }
}
