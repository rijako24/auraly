using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Planning;

public static class TurnPlanParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(string? json, out TurnPlan? plan, out string? error)
    {
        plan = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Turn plan arguments are empty.";
            return false;
        }

        try
        {
            plan = JsonSerializer.Deserialize<TurnPlan>(json, Options);
            if (plan is not null)
                return true;

            error = "Turn plan deserialized to null.";
            return false;
        }
        catch (JsonException ex)
        {
            error = $"Turn plan JSON is invalid: {ex.Message}";
            return false;
        }
    }
}
