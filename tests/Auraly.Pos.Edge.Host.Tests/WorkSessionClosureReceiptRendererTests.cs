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
        Assert.Contains("CONCILIACION POR MEDIO", receipt);
        Assert.Contains("USUARIO QUE TRABAJO: Cajero", receipt);
        Assert.Contains("CIERRE CONFIRMADO", receipt);
        Assert.Contains("  VENTAS", receipt);
        Assert.Contains("  DEVOLUCIONES", receipt);
        Assert.Contains("TRANSFERENCIA", receipt);
        Assert.Contains("TARJETA DEBITO", receipt);
        Assert.Contains("RETENCION", receipt);
        Assert.Contains("EFECTIVO ESPERADO", receipt);
        Assert.Contains("EFECTIVO CONTADO", receipt);
        Assert.DoesNotContain("MOVIMIENTO NETO TOTAL", receipt);
        Assert.Equal(1, Count(receipt, "  ENTRADAS"));
        Assert.Equal(1, Count(receipt, "  SALIDAS"));
        Assert.Equal(0, Count(receipt, "  CONTADO"));
        var transferReceipt = Section(receipt, "TRANSFERENCIA", "TARJETA DEBITO");
        Assert.DoesNotContain("ENTRADAS", transferReceipt);
        Assert.DoesNotContain("SALIDAS", transferReceipt);
        Assert.DoesNotContain("ESPERADO", transferReceipt);
        Assert.DoesNotContain("CONTADO", transferReceipt);
        Assert.DoesNotContain("DIFERENCIA DE EFECTIVO", receipt);
        Assert.Equal(1, Count(receipt, expectedLabel));
        Assert.Contains("ENTRADAS DE CAJA", receipt);
        Assert.Contains("SALIDAS DE CAJA", receipt);
        Assert.DoesNotContain(closure.WorkSessionClosureId.ToString("D"), receipt);
        Assert.True(ContainsSequence(receiptBytes, [0x1D, 0x21, 0x10]));
        Assert.True(ContainsSequence(receiptBytes, [0x1D, 0x21, 0x00]));
        Assert.Contains("Comercializadora Uno", receipt);
        Assert.Contains("SEDE: Sede principal", receipt);
        Assert.DoesNotContain("Bodega principal", receipt);

        var html = WorkSessionClosureReceiptRenderer.RenderHtml(
            closure, "Comercializadora Uno", "data:image/png;base64,AA==");
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("ARQUEO DE CAJA · CIERRE CONFIRMADO", html);
        Assert.Contains("Usuario que trabajó:", html);
        Assert.Contains("class=\"session-details\"", html);
        Assert.Contains("Todos los medios de pago", html);
        Assert.DoesNotContain("Entradas / salidas", html);
        Assert.Equal(1, Count(html, "Entradas <strong>"));
        Assert.Equal(1, Count(html, "Salidas <strong>"));
        Assert.Equal(1, Count(html, "Efectivo contado"));
        Assert.DoesNotContain("Movimiento neto total", html);
        var cashHtml = Section(html, "data-payment-method=\"Cash\"", "</section>");
        Assert.Contains("Efectivo esperado", cashHtml);
        Assert.Contains("Efectivo contado", cashHtml);
        Assert.Contains("cash-reconciliation", cashHtml);
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
        Assert.Equal(1, Count(html, expectedLabel));
        Assert.Contains("Apertura:</strong> 23/08/2026", html);
        Assert.Contains("Cierre:</strong> 23/08/2026", html);
        Assert.DoesNotContain(closure.WorkSessionClosureId.ToString("D"), html);
        Assert.Contains(expectedAmount, html);
        Assert.Contains("Comercializadora Uno", html);
        Assert.Contains("data:image/png;base64,AA==", html);
        Assert.Contains("Sede: Sede principal", html);
        Assert.DoesNotContain("Bodega principal", html);
        Assert.True(html.IndexOf("Número de ventas", StringComparison.Ordinal) < html.IndexOf("Ventas a cartera", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Ventas a cartera", StringComparison.Ordinal) < html.IndexOf("Devoluciones</td>", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Devoluciones</td>", StringComparison.Ordinal) < html.IndexOf("Total ventas", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Total ventas", StringComparison.Ordinal) < html.IndexOf("Total devoluciones", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Total devoluciones", StringComparison.Ordinal) < html.IndexOf("Valor a cartera", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Valor a cartera", StringComparison.Ordinal) < html.IndexOf("Entradas de caja", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Entradas de caja", StringComparison.Ordinal) < html.IndexOf("Salidas de caja", StringComparison.Ordinal));
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
