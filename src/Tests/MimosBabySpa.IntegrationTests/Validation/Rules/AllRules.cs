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
                ? "✅ create_reservation fue invocado."
                : "❌ create_reservation NO fue invocado aunque se esperaba una reserva exitosa.",
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
            return new TestRuleResult(true, "ℹ️ create_reservation no fue invocado — regla no aplica.", Name);

        var inOrder = log.CalledBefore("check_availability", "create_reservation");
        return new TestRuleResult(
            Passed: checkCalled && inOrder,
            Message: checkCalled && inOrder
                ? "✅ check_availability precedió a create_reservation."
                : "❌ create_reservation fue llamado SIN haber verificado disponibilidad antes.",
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
            return new TestRuleResult(true, "ℹ️ Sin reserva creada — regla no aplica.", Name);

        return new TestRuleResult(
            Passed: checkCalled,
            Message: checkCalled
                ? "✅ Disponibilidad verificada antes de confirmar."
                : "❌ Se intentó confirmar reserva sin verificar disponibilidad.",
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
                ? "✅ El bot no inventó horarios — consultó disponibilidad real."
                : "❌ El bot creó una reserva sin consultar disponibilidad (posible horario inventado).",
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
                ? "✅ No hay reservas duplicadas."
                : $"❌ Se detectaron {successfulCalls.Count} reservas exitosas — posible duplicado.",
            RuleName: Name);
    }
}
