using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Planning;

/// <summary>
/// Prevents a bare refusal from becoming an invented textual value for an optional fact.
/// A configured option that explicitly represents the refusal remains authoritative.
/// </summary>
public static partial class OptionalFactRefusalResolver
{
    public static TurnPlan Resolve(TurnPlan plan, TurnPlanScope scope)
    {
        var facts = plan.Facts
            .Where(claim => !IsUnsupportedRefusal(claim, scope))
            .ToArray();
        if (facts.Length == plan.Facts.Count)
            return plan;

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = facts,
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = plan.Response
        };
    }

    private static bool IsUnsupportedRefusal(
        PlannedFactClaim claim,
        TurnPlanScope scope)
    {
        if (!claim.Operation.Equals(TurnPlanOperations.Set, StringComparison.OrdinalIgnoreCase)
            || !scope.Facts.TryGetValue(claim.Key, out var fact)
            || fact.Required
            || fact.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase)
            || !BareRefusalRegex().IsMatch(claim.Evidence))
            return false;

        return !fact.Options.Any(option =>
            MatchesRefusal(option.Value, claim.Evidence)
            || MatchesRefusal(option.Label, claim.Evidence)
            || MatchesRefusal(option.Selector, claim.Evidence));
    }

    private static bool MatchesRefusal(string? configuredValue, string evidence) =>
        !string.IsNullOrWhiteSpace(configuredValue)
        && Normalize(configuredValue).Equals(Normalize(evidence), StringComparison.Ordinal);

    private static string Normalize(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    [GeneratedRegex(@"^\s*no\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareRefusalRegex();
}
