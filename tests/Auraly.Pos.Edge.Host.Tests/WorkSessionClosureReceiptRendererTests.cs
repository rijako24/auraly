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
            [
                new WorkSessionPaymentTotal("Cash", 100m, 10m, 5m, 95m,
                    100m + difference, difference),
                new WorkSessionPaymentTotal("Transfer", 40m, 0m, 0m, 40m),
                new WorkSessionPaymentTotal("DebitCard", 20m, 0m, 0m, 20m),
                new WorkSessionPaymentTotal("Withholding", 5m, 0m, 0m, 5m)
            ]);

        var receipt = Encoding.ASCII.GetString(
            WorkSessionClosureReceiptRenderer.Render(
                closure, 80, "Comercializadora Uno"));

        Assert.Contains(expectedLabel, receipt);
        Assert.Contains(expectedAmount, receipt);
        Assert.Contains("CONCILIACION POR MEDIO", receipt);
        Assert.Contains("USUARIO QUE TRABAJO: Cajero", receipt);
        Assert.Contains("CIERRE CONFIRMADO", receipt);
        Assert.Contains("  VENTAS", receipt);
        Assert.Contains("  DEVOLUCIONES", receipt);
        Assert.Contains("TRANSFERENCIA", receipt);
        Assert.Contains("TARJETA DEBITO", receipt);
        Assert.Contains("RETENCION", receipt);
        Assert.Contains("CONCILIACION AUTOMATICA", receipt);
        Assert.Contains("EFECTIVO ESPERADO", receipt);
        Assert.Contains("EFECTIVO CONTADO", receipt);
        Assert.Contains("Comercializadora Uno", receipt);
        Assert.Contains("SEDE: Sede principal", receipt);

        var html = WorkSessionClosureReceiptRenderer.RenderHtml(
            closure, "Comercializadora Uno", "data:image/png;base64,AA==");
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("ARQUEO DE CAJA · CIERRE CONFIRMADO", html);
        Assert.Contains("Usuario que trabajó:", html);
        Assert.Contains("Todos los medios de pago", html);
        Assert.Contains("Conciliación automática", html);
        Assert.Contains(expectedAmount, html);
        Assert.Contains("Comercializadora Uno", html);
        Assert.Contains("data:image/png;base64,AA==", html);
        Assert.Contains("Sede: Sede principal", html);
    }
}
