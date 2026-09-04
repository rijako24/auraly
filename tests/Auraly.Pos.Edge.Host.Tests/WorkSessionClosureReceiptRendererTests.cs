using System.Text;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class WorkSessionClosureReceiptRendererTests
{
    [Theory]
    [InlineData(25, "SOBRANTE", "$ 25")]
    [InlineData(-25, "FALTANTE", "$ 25")]
    [InlineData(0, "CUADRA", "$ 0")]
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

        var receiptBytes = WorkSessionClosureReceiptRenderer.Render(
            closure, 80, "Comercializadora Uno");
        var receipt = Encoding.ASCII.GetString(receiptBytes);

        Assert.Contains(expectedLabel, receipt);
        Assert.Contains(expectedAmount, receipt);
        Assert.Contains("Conciliacion por medio", receipt);
        Assert.Contains("Usuario que trabajo: Cajero", receipt);
        Assert.Contains("Cierre confirmado", receipt);
        Assert.Contains("  Ventas", receipt);
        Assert.Contains("  Devoluciones", receipt);
        Assert.Contains("Transferencia", receipt);
        Assert.Contains("Tarjeta debito", receipt);
        Assert.Contains("Retencion", receipt);
        Assert.Contains("Efectivo esperado", receipt);
        Assert.Contains("Efectivo contado", receipt);
        Assert.DoesNotContain("Movimiento neto total", receipt);
        Assert.Equal(1, Count(receipt, "  Entradas"));
        Assert.Equal(1, Count(receipt, "  Salidas"));
        Assert.Equal(0, Count(receipt, "  Contado"));
        var transferReceipt = Section(receipt, "Transferencia", "Tarjeta debito");
        Assert.DoesNotContain("Entradas", transferReceipt);
        Assert.DoesNotContain("Salidas", transferReceipt);
        Assert.DoesNotContain("Esperado", transferReceipt);
        Assert.DoesNotContain("Contado", transferReceipt);
        Assert.DoesNotContain("Diferencia de efectivo", receipt);
        Assert.Equal(1, Count(receipt, expectedLabel));
        Assert.Contains("Entradas de caja", receipt);
        Assert.Contains("Salidas de caja", receipt);
        Assert.DoesNotContain(closure.WorkSessionClosureId.ToString("D"), receipt);
        Assert.True(ContainsSequence(receiptBytes, [0x1D, 0x21, 0x10]));
        Assert.True(ContainsSequence(receiptBytes, [0x1D, 0x21, 0x00]));
        Assert.Contains("Comercializadora Uno", receipt);
        Assert.Contains("Sede: Sede principal", receipt);
        Assert.DoesNotContain("Bodega principal", receipt);
        Assert.True(ContainsSequence(receiptBytes, [0x1B, 0x61, 0x01]));
        Assert.True(ContainsSequence(receiptBytes,
            Encoding.ASCII.GetBytes("\u001bE\u0001  Efectivo esperado")));

        var html = WorkSessionClosureReceiptRenderer.RenderHtml(
            closure, "Comercializadora Uno", "data:image/png;base64,AA==");
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("data-auraly-report=\"work-session-closure\"", html);
        Assert.Contains("data-auraly-report-version=\"1\"", html);
        Assert.Contains("font:700 12px/1.4 Arial", html);
        Assert.Contains("www.auralyapp.co", html);
        Assert.Contains("Arqueo de caja · Cierre confirmado", html);
        Assert.DoesNotContain("body{text-transform:uppercase", html);
        Assert.Contains("text-transform:uppercase", html);
        Assert.Contains("text-align:center", html);
        Assert.Contains("Usuario que trabajó:", html);
        Assert.Contains("class=\"session-details\"", html);
        Assert.Contains("Detalle por medio de pago", html);
        Assert.DoesNotContain("Entradas / salidas", html);
        Assert.Equal(1, Count(html, "Entradas <strong>"));
        Assert.Equal(1, Count(html, "Salidas <strong>"));
        Assert.Equal(1, Count(html, "Efectivo contado"));
        Assert.DoesNotContain("Movimiento neto total", html);
        var cashHtml = Section(html, "data-payment-method=\"Cash\"", "</section>");
        Assert.DoesNotContain("Efectivo esperado", cashHtml);
        Assert.DoesNotContain("Efectivo contado", cashHtml);
        Assert.DoesNotContain("cash-reconciliation", cashHtml);
        var transferHtml = Section(html, "data-payment-method=\"Transfer\"", "</section>");
        var cardHtml = Section(html, "data-payment-method=\"DebitCard\"", "</section>");
        Assert.DoesNotContain("Entradas <strong>", transferHtml);
        Assert.DoesNotContain("Salidas <strong>", transferHtml);
        Assert.DoesNotContain("Esperado", transferHtml);
        Assert.DoesNotContain("Contado", transferHtml);
        Assert.DoesNotContain("Entradas <strong>", cardHtml);
        Assert.DoesNotContain("Salidas <strong>", cardHtml);
        Assert.DoesNotContain("Esperado", cardHtml);
        Assert.DoesNotContain("Contado", cardHtml);
        Assert.DoesNotContain("Diferencia de efectivo", html);
        Assert.Contains("Totales del turno", html);
        Assert.Equal(1, Count(html, expectedLabel));
        Assert.Contains("Apertura:</strong> 23/08/2026", html);
        Assert.Contains("Cierre:</strong> 23/08/2026", html);
        Assert.DoesNotContain(closure.WorkSessionClosureId.ToString("D"), html);
        Assert.Contains(expectedAmount, html);
        Assert.Contains("Comercializadora Uno", html);
        Assert.Contains("data:image/png;base64,AA==", html);
        Assert.Contains("Sede: Sede principal", html);
        Assert.DoesNotContain("Bodega principal", html);
        Assert.Contains("body{width:80mm", html);
        Assert.Contains("padding:5mm 3mm 2mm 2mm", html);
        Assert.True(html.IndexOf("Número de ventas", StringComparison.Ordinal) < html.IndexOf("Ventas a cartera", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Ventas a cartera", StringComparison.Ordinal) < html.IndexOf("Devoluciones</td>", StringComparison.Ordinal));
        var totalsHtml = Section(html, "Totales del turno", "<div class=\"difference\">");
        Assert.True(totalsHtml.IndexOf(">Ventas</td>", StringComparison.Ordinal) < totalsHtml.IndexOf(">Devoluciones</td>", StringComparison.Ordinal));
        Assert.True(totalsHtml.IndexOf(">Devoluciones</td>", StringComparison.Ordinal) < totalsHtml.IndexOf("Valor a cartera", StringComparison.Ordinal));
        Assert.True(totalsHtml.IndexOf("Valor a cartera", StringComparison.Ordinal) < totalsHtml.IndexOf("Entradas de caja", StringComparison.Ordinal));
        Assert.True(totalsHtml.IndexOf("Entradas de caja", StringComparison.Ordinal) < totalsHtml.IndexOf("Salidas de caja", StringComparison.Ordinal));
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string Section(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"No se encontró el inicio '{start}'.");
        var endIndex = value.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"No se encontró el final '{end}'.");
        return value[startIndex..endIndex];
    }

    private static bool ContainsSequence(byte[] value, byte[] expected) =>
        value.AsSpan().IndexOf(expected) >= 0;
}
