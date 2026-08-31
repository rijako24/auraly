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
            "Entregado por administración");
        var session = new PosLocalUserSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cajero", "Carol Cairo",
            [], DateTimeOffset.MaxValue, "token");
        var workstation = new PosWorkstationIdentity(
            "02", "Bodega de venta", "Principal", "Carol Cairo", "Auraly", null);

        var raw = Encoding.ASCII.GetString(
            PosCashMovementTicketPrinter.RenderRaw(ticket, session, workstation, 80));
        var html = PosCashMovementTicketPrinter.RenderHtml(ticket, session, workstation);

        Assert.Contains(expectedTitle, raw);
        Assert.Contains("MOTIVO: Base inicial", raw);
        Assert.Contains("FIRMA:", raw);
        Assert.Contains("Carol Cairo", raw);
        Assert.Contains(expectedTitle, html);
        Assert.Contains("Entregado por administraci", html);
        Assert.Contains("class=\"signature\"", html);
    }
}
