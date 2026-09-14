using Auraly.Contracts.Sales;
using Auraly.Pos.Printing;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class CreditSaleAcknowledgementRendererTests
{
    [Theory]
    [InlineData("Receipt", 80)]
    [InlineData("Receipt", 58)]
    [InlineData("HalfLetter", 80)]
    [InlineData("HalfLegal", 80)]
    [InlineData("Letter", 80)]
    public void Every_invoice_format_has_a_complete_professional_credit_acknowledgement(
        string format,
        int width)
    {
        var html = new CreditSaleAcknowledgementRenderer().Render(
            Acknowledgement(), format, width);

        Assert.Contains("data-auraly-report=\"credit-sale-acknowledgement\"", html);
        Assert.Contains("FV-100", html);
        Assert.Contains("Cliente Uno", html);
        Assert.Contains("900123456", html);
        Assert.Contains("50.000", html);
        Assert.Contains("150.000", html);
        Assert.Contains("Cajero Principal", html);
        Assert.Contains("Firma del cliente", html);
        Assert.Contains("Identificación", html);
    }

    internal static CreditSaleAcknowledgement Acknowledgement() => new(
        Guid.NewGuid(),
        "FV-100",
        new DateTimeOffset(2026, 9, 13, 10, 30, 0, TimeSpan.FromHours(-5)),
        "Cliente Uno",
        "900123456",
        50_000m,
        150_000m,
        "Cajero Principal",
        "Megafruver",
        BusinessName: "Sede Principal",
        WarehouseName: "Bodega de venta");
}
