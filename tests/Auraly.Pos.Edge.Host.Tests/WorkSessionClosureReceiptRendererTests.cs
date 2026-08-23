using System.Text;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class WorkSessionClosureReceiptRendererTests
{
    [Theory]
    [InlineData(25, "DIFERENCIA (+) SOBRANTE", "+25.00")]
    [InlineData(-25, "DIFERENCIA (-) FALTANTE", "-25.00")]
    [InlineData(0, "DIFERENCIA (CUADRADO)", "0.00")]
    public void Receipt_identifies_positive_negative_and_balanced_differences(
        decimal difference,
        string expectedLabel,
        string expectedAmount)
    {
        var now = new DateTimeOffset(2026, 8, 23, 15, 0, 0, TimeSpan.Zero);
        var closure = new WorkSessionClosureView(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Sede principal",
            Guid.NewGuid(), "Bodega principal", Guid.NewGuid(), "Cajero",
            null, now.AddHours(-8), now, 100m, 10m, 5m, 95m, 100m,
            100m + difference, difference, "Conteo de prueba",
            [new WorkSessionPaymentTotal("Cash", 100m, 10m, 5m, 95m)]);

        var receipt = Encoding.ASCII.GetString(
            WorkSessionClosureReceiptRenderer.Render(closure, 80));

        Assert.Contains(expectedLabel, receipt);
        Assert.Contains(expectedAmount, receipt);
        Assert.Contains("TOTALES POR MEDIO", receipt);
        Assert.Contains("EFECTIVO ESPERADO", receipt);
        Assert.Contains("EFECTIVO CONTADO", receipt);
    }
}
