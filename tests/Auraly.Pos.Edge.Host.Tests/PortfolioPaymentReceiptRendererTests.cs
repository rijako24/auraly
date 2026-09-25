using Auraly.Pos.Printing;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PortfolioPaymentReceiptRendererTests
{
    [Theory]
    [InlineData("Receivable", 58, false)]
    [InlineData("Receivable", 80, false)]
    [InlineData("Payable", 58, true)]
    [InlineData("Payable", 80, true)]
    public void Renders_direction_and_width_with_real_payment_details(
        string direction, int width, bool requiresSignature)
    {
        var receipt = Sample(direction);

        var html = new PortfolioPaymentReceiptRenderer().Render(receipt, width);

        Assert.Contains($"@page{{size:{width}mm auto", html);
        Assert.Contains(receipt.DocumentNumber, html);
        Assert.Contains("900123456-7", html);
        Assert.Contains("24/09/2026 17:42", html);
        Assert.Contains("VTA-123", html);
        Assert.Contains("Transferencia", html);
        Assert.Contains("BAN-88291", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Equal(requiresSignature, html.Contains("Firma de recibido"));
        Assert.Contains("Cliente &lt;prueba&gt;", html);
    }

    [Fact]
    public void Rejects_totals_that_differ_from_confirmed_payment()
    {
        var receipt = Sample("Payable") with { TotalAmount = 120_000m };

        Assert.Throws<ArgumentException>(() =>
            new PortfolioPaymentReceiptRenderer().Render(receipt, 80));
    }

    private static PortfolioPaymentReceipt Sample(string direction) => new(
        Guid.Parse("30000000-0000-0000-0000-000000000003"), direction,
        "ABO-123", new DateTimeOffset(2026, 9, 24, 22, 42, 0, TimeSpan.Zero),
        "Empresa", "Empresa SAS", "900123456", "7", null,
        "Sede", "Calle 1", "3001234567", "Cliente <prueba>", "CC 123",
        "Laura", 125_000m,
        [new("VTA-123", 125_000m)],
        [new("Efectivo", 100_000m, null), new("Transferencia", 25_000m, "BAN-88291")]);
}
