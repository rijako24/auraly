using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed record OptionSelectorReference(FactSchemaEntry Fact, FactValueOption Option);

public static class OptionSelectorReferenceDetector
{
    public static IReadOnlyList<OptionSelectorReference> Find(TurnPlanScope scope, string message) =>
        scope.Facts.Values
            .SelectMany(fact => fact.Options
                .Where(option => !string.IsNullOrWhiteSpace(option.Selector)
                    && Appears(message, option.Selector!))
                .Select(option => new OptionSelectorReference(fact, option)))
            .ToList();

    private static bool Appears(string message, string selector)
    {
        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(selector.Trim())}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}