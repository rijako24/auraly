using Auraly.Pos.Printing;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PortfolioPaymentReceiptRendererTests
{
    [Theory]
    [InlineData("Payable", "Accepted")]
    [InlineData("Receivable", "Accepted")]
    [InlineData("Payable", null)]
    [InlineData("Receivable", null)]
    [InlineData("Payable", "Processed")]
    [InlineData("Receivable", "Processed")]
    public void Print_keeps_the_acknowledged_payment_state_in_every_format(string direction, string? status)
    {
        var receipt = Sample(direction) with { Status = status };
        foreach (var format in new[] { "Receipt", "HalfLetter", "HalfLegal", "Letter" })
        {
            var html = new PortfolioPaymentReceiptRenderer().Render(receipt, format, 80);
            var applied = status == "Processed";
            Assert.Contains(format == "Receipt" ? "data-auraly-report-version=\"3\"" : "data-auraly-report-version=\"4\"", html);
            Assert.Equal(applied, html.Contains("Facturas aplicadas"));
            Assert.Equal(applied, html.Contains("Abono aplicado"));
            Assert.Equal(applied && direction == "Payable", html.Contains("Valor pagado"));
            Assert.Equal(applied && direction == "Payable", html.Contains("Firma de recibido"));
            Assert.Equal(!applied, html.Contains("Este registro no confirma un abono aplicado"));
            if (status == "Accepted") Assert.Contains("pendiente de aplicar", html);
            if (status is null)
            {
                Assert.Contains("Aplicaci&#243;n sin confirmar", html);
                Assert.Contains("Documentos relacionados", html);
            }
        }
    }

    [Fact]
    public void Rejected_payments_cannot_be_printed_as_an_acknowledged_payment()
    {
        Assert.Throws<ArgumentException>(() => new PortfolioPaymentReceiptRenderer().Render(
            Sample("Payable") with { Status = "Rejected" }, 80));
    }

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

    [Theory]
    [InlineData("HalfLetter", "215.9mm 139.7mm")]
    [InlineData("HalfLegal", "215.9mm 165.1mm")]
    [InlineData("Letter", "Letter portrait")]
    public void Sheet_formats_keep_the_same_payment_and_allocation_details(
        string format, string pageSize)
    {
        var html = new PortfolioPaymentReceiptRenderer().Render(Sample("Payable"), format, 80);

        Assert.Contains($"data-auraly-format=\"{format}\"", html);
        Assert.Contains($"@page{{size:{pageSize}", html);
        Assert.Contains("VTA-123", html);
        Assert.Contains("BAN-88291", html);
        Assert.Contains("Firma de recibido", html);
    }

    private static PortfolioPaymentReceipt Sample(string direction) => new(
        Guid.Parse("30000000-0000-0000-0000-000000000003"), direction,
        "ABO-123", new DateTimeOffset(2026, 9, 24, 22, 42, 0, TimeSpan.Zero),
        "Empresa", "Empresa SAS", "900123456", "7", null,
        "Sede", "Calle 1", "3001234567", "Cliente <prueba>", "CC 123",
        "Laura", 125_000m,
        [new("VTA-123", 125_000m)],
        [new("Efectivo", 100_000m, null), new("Transferencia", 25_000m, "BAN-88291")], "Processed");
}
