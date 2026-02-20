using MimosBabySpa.Application.Tools;
using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Validation.Rules;

public class ReservationMustCallCreateReservationRule : ITestRule
{
    public string Name => "ReservationMustCallCreateReservation";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var called = log.WasCalled(ToolType.CreateReservation);
        return new TestRuleResult(
            Passed:  called,
            Message: called ? "✅ CreateReservation fue invocado." : "❌ CreateReservation NO fue invocado aunque se esperaba una reserva exitosa.",
            RuleName: Name);
    }
}

public class CheckAvailabilityBeforeCreateReservationRule : ITestRule
{
    public string Name => "CheckAvailabilityBeforeCreateReservation";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var checkCalled  = log.WasCalled(ToolType.CheckAvailability);
        var createCalled = log.WasCalled(ToolType.CreateReservation);

        if (!createCalled)
            return new TestRuleResult(true, "ℹ️ CreateReservation no fue invocado — regla no aplica.", Name);

        var inOrder = log.CheckAvailabilityCalledBefore(ToolType.CreateReservation);
        return new TestRuleResult(
            Passed:  checkCalled && inOrder,
            Message: checkCalled && inOrder
                ? "✅ CheckAvailability precedió a CreateReservation."
                : "❌ CreateReservation fue llamado SIN haber verificado disponibilidad antes.",
            RuleName: Name);
    }
}

public class NoConfirmationWithoutAvailabilityCheckRule : ITestRule
{
    public string Name => "NoConfirmationWithoutAvailabilityCheck";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var createCalled = log.WasCalled(ToolType.CreateReservation);
        var checkCalled  = log.WasCalled(ToolType.CheckAvailability);

        if (!createCalled)
            return new TestRuleResult(true, "ℹ️ Sin reserva creada — regla no aplica.", Name);

        return new TestRuleResult(
            Passed:  checkCalled,
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
        var reservationCalls = log.AllCalls(ToolType.CreateReservation);
        var availabilityCalls = log.AllCalls(ToolType.CheckAvailability);

        // If a reservation was made, the system must have called availability first
        var passed = reservationCalls.Count == 0 || availabilityCalls.Count > 0;
        return new TestRuleResult(
            Passed:  passed,
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
        var reservationCalls = log.AllCalls(ToolType.CreateReservation);
        var successfulCalls  = reservationCalls.Where(c => c.Result.Success).ToList();

        var duplicates = successfulCalls.Count > 1;
        return new TestRuleResult(
            Passed:  !duplicates,
            Message: !duplicates
                ? "✅ No hay reservas duplicadas."
                : $"❌ Se detectaron {successfulCalls.Count} reservas exitosas — posible duplicado.",
            RuleName: Name);
    }
}
