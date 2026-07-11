using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Planning;

public static class TurnPlanNormalizer
{
    public static TurnPlan Normalize(TurnPlan plan, TurnPlanScope scope)
    {
        var facts = plan.Facts.Select(claim =>
        {
            if (!scope.Facts.TryGetValue(claim.Key, out var definition)
                || !definition.Type.Equals("phone", StringComparison.OrdinalIgnoreCase)
                || claim.Value.ValueKind != JsonValueKind.String)
                return claim;

            var raw = claim.Value.GetString()?.Trim() ?? string.Empty;
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            var canonical = raw.StartsWith('+') ? $"+{digits}" : digits;
            return new PlannedFactClaim
            {
                Key = claim.Key,
                Operation = claim.Operation,
                Value = JsonSerializer.SerializeToElement(canonical),
                Evidence = claim.Evidence
            };
        }).ToArray();

        var ambiguous = new HashSet<string>(plan.Response.AmbiguousFields, StringComparer.OrdinalIgnoreCase);
        foreach (var signal in plan.Signals)
        {
            if (!scope.Signals.TryGetValue(signal.Type, out var definition))
                continue;
            foreach (var rule in definition.AmbiguityRules)
            {
                if (!rule.Type.Equals("distinct_values", StringComparison.OrdinalIgnoreCase)
                    || signal.Value.ValueKind != JsonValueKind.Array)
                    continue;

                var values = signal.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty(rule.ValueProperty, out _))
                    .Select(item => item.GetProperty(rule.ValueProperty))
                    .Where(value => value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    .Select(value => value.ValueKind == JsonValueKind.String
                        ? value.GetString()?.Trim()
                        : value.GetRawText())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (values >= rule.MinimumDistinctValues)
                    ambiguous.Add(rule.Field);
            }
        }

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = facts,
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective
            {
                Mode = ambiguous.Count > 0 ? "ask_clarification" : plan.Response.Mode,
                AmbiguousFields = ambiguous.ToArray()
            }
        };
    }
}
