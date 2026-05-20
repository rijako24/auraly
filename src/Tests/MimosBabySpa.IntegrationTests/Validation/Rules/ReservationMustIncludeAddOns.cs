using System.Text.Json;
using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Validation.Rules;

public class ReservationMustIncludeAddOns : ITestRule
{
    public string Name => "ReservationMustIncludeAddOns";

    public TestRuleResult Evaluate(ToolCallLog log)
    {
        var createCall = log.AllCalls("create_reservation")
            .FirstOrDefault(c => !c.ResultIsError);

        if (createCall == null)
            return new TestRuleResult(true, "No se creó reserva exitosa, regla no aplica.", Name);

        // Verificar en los argumentos si se pasaron add_ons
        try
        {
            using var doc = JsonDocument.Parse(createCall.ArgumentsJson);
            if (doc.RootElement.TryGetProperty("add_ons", out var addOns) &&
                addOns.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(addOns.GetString()))
            {
                return new TestRuleResult(true, $"✅ La reserva incluye add-ons: {addOns.GetString()}", Name);
            }
        }
        catch { /* continúa */ }

        return new TestRuleResult(false,
            "❌ La reserva se creó SIN add-ons aunque el escenario los requería.", Name);
    }
}
