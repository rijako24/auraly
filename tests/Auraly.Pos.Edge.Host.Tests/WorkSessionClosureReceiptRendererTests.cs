using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Printing;
using System.Text.Json;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class WorkSessionClosureReceiptRendererTests
{
    [Fact]
    public void Version_two_uses_the_requested_sections_order_and_one_reconciliation_box_per_counted_method()
    {
        var closure = Closure(2);

        var html = WorkSessionClosureReceiptRenderer.RenderHtml(
            closure, "Comercializadora Uno", "data:image/png;base64,AA==");

        Assert.Contains("data-auraly-report-version=\"2\"", html);
        Assert.True(Index(html, "Actividad del turno") < Index(html, "Totales del turno"));
        Assert.True(Index(html, "Totales del turno") < Index(html, "Ventas a cartera</h2>"));
        Assert.True(Index(html, "Ventas a cartera</h2>") < Index(html, "Detalle por medio de pago"));

        var totals = Section(html, "Totales del turno", "Ventas a cartera</h2>");
        Assert.DoesNotContain("Efectivo esperado", totals);
        Assert.DoesNotContain("Efectivo contado", totals);

        var payments = Section(html, "Detalle por medio de pago", "Observación:");
        Assert.True(Index(payments, "data-payment-method=\"Card\"") < Index(payments, "data-payment-method=\"Transfer\""));
        Assert.True(Index(payments, "data-payment-method=\"Transfer\"") < Index(payments, "data-payment-method=\"Cash\""));
        Assert.Equal(3, Count(payments, "class=\"difference\""));
        Assert.Contains("SOBRANTE", Section(payments, "data-payment-method=\"Card\"", "</section>"));
        Assert.Contains("FALTANTE", Section(payments, "data-payment-method=\"Transfer\"", "</section>"));
        Assert.Contains("CUADRA", Section(payments, "data-payment-method=\"Cash\"", "</section>"));

        var credit = Section(html, "Ventas a cartera</h2>", "Detalle por medio de pago");
        Assert.Contains("Cliente Uno", credit);
        Assert.Contains("FV-10", credit);
        Assert.Contains("$ 25", credit);
        Assert.Contains("Total cartera", credit);
    }

    [Fact]
    public void Version_two_rejects_a_credit_detail_that_does_not_reconcile()
    {
        var closure = Closure(2) with { CreditSalesAmount = 26m };

        var error = Assert.Throws<InvalidDataException>(() =>
            WorkSessionClosureReceiptRenderer.RenderHtml(closure));

        Assert.Contains("no coincide", error.Message);
    }

    [Fact]
    public void Version_three_details_entries_and_exits_and_reconciles_each_cash_total()
    {
        var html = WorkSessionClosureReceiptRenderer.RenderHtml(Closure(3));

        Assert.Contains("data-auraly-report-version=\"3\"", html);
        var entries = Section(html, "Entradas de dinero", "Salidas de dinero");
        Assert.Contains("Ingreso adicional", entries);
        Assert.Contains("MOV-ENTRADA", entries);
        Assert.Contains("Cajero", entries);
        Assert.Contains("Total", entries);
        Assert.Contains("$ 7", entries);
        var exits = Section(html, "Salidas de dinero", "Detalle por medio de pago");
        Assert.Contains("Compra menor", exits);
        Assert.Contains("MOV-SALIDA", exits);
        Assert.Contains("$ 2", exits);
    }

    [Fact]
    public void Version_three_never_blocks_printing_and_totals_the_rendered_detail()
    {
        var closure = Closure(3) with
        {
            PaymentTotals = Closure(3).PaymentTotals.Select(total =>
                total.PaymentMethodCode == "Cash"
                    ? total with { CashEntryAmount = 8m }
                    : total).ToArray()
        };

        var html = WorkSessionClosureReceiptRenderer.RenderHtml(closure);

        var entries = Section(html, "Entradas de dinero", "Salidas de dinero");
        Assert.Contains("$ 7", entries);
        Assert.DoesNotContain("$ 8", entries);
    }

    [Fact]
    public void Version_one_remains_available_for_historical_reprints()
    {
        var html = WorkSessionClosureReceiptRenderer.RenderHtml(Closure(1));

        Assert.Contains("data-auraly-report-version=\"1\"", html);
        Assert.True(Index(html, "Detalle por medio de pago") < Index(html, "Totales del turno"));
        Assert.Contains("Efectivo esperado", Section(html, "Totales del turno", "class=\"difference\""));
    }

    [Fact]
    public void Historical_snapshots_without_a_template_field_deserialize_as_version_one()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(Closure(1), options)
            .Replace(",\"receiptTemplateVersion\":1", string.Empty, StringComparison.Ordinal);

        var restored = JsonSerializer.Deserialize<WorkSessionClosureView>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.ReceiptTemplateVersion);
    }

    private static WorkSessionClosureView Closure(int version)
    {
        var now = new DateTimeOffset(2026, 8, 23, 15, 0, 0, TimeSpan.Zero);
        var cashMovements = version < 3
            ? null
            : new WorkSessionCashMovementDetail[]
            {
                new(Guid.NewGuid(), "In", "MOV-ENTRADA", "Ingreso adicional",
                    7m, now.AddHours(-2), "Cajero", "REF-1"),
                new(Guid.NewGuid(), "Out", "MOV-SALIDA", "Compra menor",
                    2m, now.AddHours(-1), "Cajero")
            };
        return new WorkSessionClosureView(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Sede principal",
            Guid.NewGuid(), "Bodega principal", Guid.NewGuid(), "Cajero",
            null, now.AddHours(-8), now, 165m, 10m, 5m, 160m, 95m,
            95m, 0m, "Conteo de prueba",
            [
                new WorkSessionPaymentTotal("Cash", 100m, 10m, 5m, 95m, 95m, 0m, true,
                    version < 3 ? 0m : 7m, version < 3 ? 0m : 2m),
                new WorkSessionPaymentTotal("Transfer", 40m, 0m, 0m, 40m, 35m, -5m, true),
                new WorkSessionPaymentTotal("Card", 25m, 0m, 0m, 25m, 30m, 5m, true)
            ],
            3, 1, 25m, 0,
            [new WorkSessionCreditSale("Cliente Uno", "FV-10", 25m)],
            version,
            cashMovements);
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static int Index(string value, string fragment)
    {
        var result = value.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(result >= 0, $"No se encontró '{fragment}'.");
        return result;
    }

    private static string Section(string value, string start, string end)
    {
        var startIndex = Index(value, start);
        var endIndex = value.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"No se encontró el final '{end}'.");
        return value[startIndex..endIndex];
    }
}
