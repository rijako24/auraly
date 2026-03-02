using MimosBabySpa.Application.Tools;
using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Validation.Rules;

public class ReservationMustIncludeAddOns : ITestRule
{
    public string Name => "ReservationMustIncludeAddOns";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        // Find the CreateReservation call
        var createCall = log.All
            .FirstOrDefault(c => c.ToolType == ToolType.CreateReservation);

        if (createCall == null)
        {
            // If checking availability was expected but not done, that's another rule's job.
            // But if we require add-ons, we implicitly require a reservation.
            // Let's return Pass here and let "ReservationMustCallCreateReservation" handle the missing call.
            return new TestRuleResult(true, "No se creó reserva, regla no aplica.", Name);
        }

        if (createCall.Result == null || !createCall.Result.Success)
        {
            return new TestRuleResult(true, "Reserva falló, reglas de contenido no aplican.", Name);
        }

        // Check if the success message contains "Extras:" (servicios extras incluidos en la reserva)
        // The handler format: "✓ Reserva confirmada exitosamente... Extras: Masaje Extra..."
        if (createCall.Result.Message.Contains("Extras:", StringComparison.OrdinalIgnoreCase))
        {
            return new TestRuleResult(true, "La reserva incluye servicios extras.", Name);
        }

        return new TestRuleResult(false, 
            $"La reserva {createCall.Result.Data?["reservation_id"]} se creó SIN servicios extras (mensaje: {createCall.Result.Message}).", Name);
    }
}
