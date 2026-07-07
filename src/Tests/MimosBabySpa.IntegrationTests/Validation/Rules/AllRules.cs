using System.Text.Json;
using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Validation.Rules;

public class ReservationMustCallCreateReservationRule : ITestRule
{
    public string Name => "ReservationMustCallCreateReservation";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var called = log.WasCalled("create_reservation");
        return new TestRuleResult(
            Passed: called,
            Message: called
                ? "PASS create_reservation fue invocado."
                : "FAIL create_reservation NO fue invocado aunque se esperaba una reserva exitosa.",
            RuleName: Name);
    }
}

public class CheckAvailabilityBeforeCreateReservationRule : ITestRule
{
    public string Name => "CheckAvailabilityBeforeCreateReservation";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var checkCalled = log.WasCalled("check_availability");
        var createCalled = log.WasCalled("create_reservation");

        if (!createCalled)
            return new TestRuleResult(true, "INFO create_reservation no fue invocado; regla no aplica.", Name);

        var inOrder = log.CalledBefore("check_availability", "create_reservation");
        return new TestRuleResult(
            Passed: checkCalled && inOrder,
            Message: checkCalled && inOrder
                ? "PASS check_availability precedio a create_reservation."
                : "FAIL create_reservation fue llamado SIN haber verificado disponibilidad antes.",
            RuleName: Name);
    }
}

public class NoConfirmationWithoutAvailabilityCheckRule : ITestRule
{
    public string Name => "NoConfirmationWithoutAvailabilityCheck";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var createCalled = log.WasCalled("create_reservation");
        var checkCalled = log.WasCalled("check_availability");

        if (!createCalled)
            return new TestRuleResult(true, "INFO Sin reserva creada; regla no aplica.", Name);

        return new TestRuleResult(
            Passed: checkCalled,
            Message: checkCalled
                ? "PASS Disponibilidad verificada antes de confirmar."
                : "FAIL Se intento confirmar reserva sin verificar disponibilidad.",
            RuleName: Name);
    }
}

public class BotMustNotInventTimeSlotsRule : ITestRule
{
    public string Name => "BotMustNotInventTimeSlots";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var reservationCalls = log.AllCalls("create_reservation");
        var availabilityCalls = log.AllCalls("check_availability");

        var passed = reservationCalls.Count == 0 || availabilityCalls.Count > 0;
        return new TestRuleResult(
            Passed: passed,
            Message: passed
                ? "PASS El bot no invento horarios; consulto disponibilidad real."
                : "FAIL El bot creo una reserva sin consultar disponibilidad (posible horario inventado).",
            RuleName: Name);
    }
}

public class NoDuplicateReservationRule : ITestRule
{
    public string Name => "NoDuplicateReservation";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var reservationCalls = log.AllCalls("create_reservation");
        var successfulCalls = reservationCalls.Where(c => !c.ResultIsError).ToList();

        var duplicates = successfulCalls.Count > 1;
        return new TestRuleResult(
            Passed: !duplicates,
            Message: !duplicates
                ? "PASS No hay reservas duplicadas."
                : $"FAIL Se detectaron {successfulCalls.Count} reservas exitosas; posible duplicado.",
            RuleName: Name);
    }
}

public class MultipleReservationCyclesRule : ITestRule
{
    public string Name => "MultipleReservationCycles";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var all = log.All.ToList();
        var successfulCreates = all
            .Select((record, index) => (record, index))
            .Where(x => IsTool(x.record, "create_reservation") && !x.record.ResultIsError)
            .ToList();

        if (successfulCreates.Count < 2)
        {
            var createCalls = all.Where(record => IsTool(record, "create_reservation")).ToList();
            var callSummary = createCalls.Count == 0
                ? "no create_reservation calls were logged"
                : string.Join("; ", createCalls.Select(c => $"error={c.ResultIsError} result={c.ResultJson}"));
            return new TestRuleResult(
                false,
                $"Expected at least 2 successful reservations; found {successfulCreates.Count}. {callSummary}",
                Name);
        }

        var signatures = successfulCreates
            .Select(x => ReservationSignature(x.record.ArgumentsJson))
            .ToList();

        if (signatures.Distinct(StringComparer.OrdinalIgnoreCase).Count() < successfulCreates.Count)
        {
            return new TestRuleResult(
                false,
                "Successful reservations were not distinct; this looks like the same slot duplicated.",
                Name);
        }

        var previousCreateIndex = -1;
        foreach (var create in successfulCreates)
        {
            var hasAvailabilityInCycle = all
                .Select((record, index) => (record, index))
                .Any(x => x.index > previousCreateIndex
                    && x.index < create.index
                    && IsTool(x.record, "check_availability")
                    && !x.record.ResultIsError);

            if (!hasAvailabilityInCycle)
            {
                return new TestRuleResult(
                    false,
                    "A reservation was created without an availability check in its own cycle.",
                    Name);
            }

            previousCreateIndex = create.index;
        }

        return new TestRuleResult(
            true,
            "The customer completed multiple reservation cycles in one conversation, each with its own availability check.",
            Name);
    }

    private static bool IsTool(ToolCallRecord record, string toolName) =>
        string.Equals(record.ToolName, toolName, StringComparison.OrdinalIgnoreCase);

    private static string ReservationSignature(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            return string.Join("|", Get(root, "service"), Get(root, "date"), Get(root, "time"));
        }
        catch
        {
            return argumentsJson;
        }
    }

    private static string Get(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) ? value.ToString() : string.Empty;
}
