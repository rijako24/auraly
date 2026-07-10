using System.Text.Json;
using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Validation.Rules;

public class PrepareCheckoutCalledAtLeastTwiceRule : ITestRule
{
    public string Name => "PrepareCheckoutCalledAtLeastTwice";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var count = log.CallCount("prepare_checkout");
        return new TestRuleResult(
            count >= 2,
            count >= 2
                ? $"PASS prepare_checkout fue invocado {count} veces."
                : $"FAIL Se esperaban al menos 2 prepare_checkout; se invoco {count}.",
            Name);
    }
}

public class LastCheckoutIncludesMasajeExtraRule : ITestRule
{
    public string Name => "LastCheckoutIncludesMasajeExtra";

    public TestRuleResult Evaluate(ToolCallLog log) => CheckoutSnapshotRule.ExpectLast(
        log,
        Name,
        expectedService: "Plan Deluxe",
        expectedDate: "2026-09-15",
        expectedTime: "10:00",
        expectedAddOns: "Masaje Extra 15m",
        expectedAmountInCents: 14000);
}

public class LastCheckoutRemovesAddOnsRule : ITestRule
{
    public string Name => "LastCheckoutRemovesAddOns";

    public TestRuleResult Evaluate(ToolCallLog log) => CheckoutSnapshotRule.ExpectLast(
        log,
        Name,
        expectedService: "Plan Deluxe",
        expectedDate: "2026-09-15",
        expectedTime: "10:00",
        expectedAddOns: "ninguno",
        expectedAmountInCents: 12000);
}

public class LastCheckoutUsesChangedBookingInputsRule : ITestRule
{
    public string Name => "LastCheckoutUsesChangedBookingInputs";

    public TestRuleResult Evaluate(ToolCallLog log) => CheckoutSnapshotRule.ExpectLast(
        log,
        Name,
        expectedService: "Plan Marineritos",
        expectedDate: "2026-09-16",
        expectedTime: "11:00",
        expectedAddOns: "ninguno",
        expectedAmountInCents: 12500000);
}

internal static class CheckoutSnapshotRule
{
    public static TestRuleResult ExpectLast(
        ToolCallLog log,
        string ruleName,
        string expectedService,
        string expectedDate,
        string expectedTime,
        string expectedAddOns,
        long expectedAmountInCents)
    {
        var call = log.LastCall("prepare_checkout");
        if (call is null)
            return new TestRuleResult(false, "FAIL prepare_checkout no fue invocado.", ruleName);

        if (call.ResultIsError)
            return new TestRuleResult(false, $"FAIL Ultimo prepare_checkout fallo: {call.ResultJson}. Facts: {call.FactsJson}", ruleName);

        if (string.IsNullOrWhiteSpace(call.ActivePaymentCheckoutSnapshotJson))
            return new TestRuleResult(false, $"FAIL No se registro snapshot de pago activo para prepare_checkout. Facts: {call.FactsJson}", ruleName);

        try
        {
            using var doc = JsonDocument.Parse(call.ActivePaymentCheckoutSnapshotJson);
            var root = doc.RootElement;
            var service = GetString(root, "service_name");
            var date = GetString(root, "reservation_date");
            var time = GetString(root, "reservation_time");
            var addOns = root.TryGetProperty("facts", out var facts)
                ? GetString(facts, "add_ons")
                : string.Empty;

            var passed = string.Equals(service, expectedService, StringComparison.OrdinalIgnoreCase)
                && string.Equals(date, expectedDate, StringComparison.OrdinalIgnoreCase)
                && string.Equals(time, expectedTime, StringComparison.OrdinalIgnoreCase)
                && string.Equals(addOns, expectedAddOns, StringComparison.OrdinalIgnoreCase)
                && call.ActivePaymentAmountInCents == expectedAmountInCents;

            return new TestRuleResult(
                passed,
                passed
                    ? "PASS Ultimo checkout usa los datos modificados esperados."
                    : $"FAIL Checkout final inesperado. service={service}, date={date}, time={time}, add_ons={addOns}, amount={call.ActivePaymentAmountInCents}.",
                ruleName);
        }
        catch (Exception ex)
        {
            return new TestRuleResult(false, $"FAIL Snapshot de checkout invalido: {ex.Message}", ruleName);
        }
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
}

