using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents;

internal static class FactValueShapeMatcher
{
    public static bool MessageMatchesFactShape(
        IReadOnlyList<FactSchemaEntry>? schema,
        string factKey,
        string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var entry = schema?
            .FirstOrDefault(candidate => candidate.Key.Equals(factKey, StringComparison.OrdinalIgnoreCase));
        var type = entry?.Type;

        if (string.IsNullOrWhiteSpace(type))
            return !string.IsNullOrWhiteSpace(message);

        return type.Trim().ToLowerInvariant() switch
        {
            "time" => TimeOnly.TryParse(NormalizeTimeText(message), out _),
            "date" => DateOnly.TryParse(message, out _)
                || message.Any(char.IsDigit)
                || ContainsConfiguredAlias(entry, message),
            "phone" => message.Count(char.IsDigit) >= 7,
            "email" => message.Contains('@', StringComparison.Ordinal),
            "number" => decimal.TryParse(
                message,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out _),
            _ => entry?.Aliases.Count > 0 ? ContainsConfiguredAlias(entry, message) : !string.IsNullOrWhiteSpace(message)
        };
    }

    private static string NormalizeTimeText(string message)
    {
        var trimmed = message.Trim();
        if (TimeOnly.TryParse(trimmed, out _))
            return trimmed;

        var match = Regex.Match(
            trimmed,
            @"(?<!\d)(?<hour>\d{1,2})(?:\s*[:.]\s*(?<minute>\d{2}))?\s*(?<period>a\.?\s*m\.?|p\.?\s*m\.?|am|pm)?(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return trimmed;

        if (!int.TryParse(match.Groups["hour"].Value, out var hour))
            return trimmed;

        var minute = 0;
        if (match.Groups["minute"].Success
            && !int.TryParse(match.Groups["minute"].Value, out minute))
        {
            return trimmed;
        }

        var period = match.Groups["period"].Value.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        if (period == "pm" && hour < 12)
            hour += 12;
        else if (period == "am" && hour == 12)
            hour = 0;

        return $"{hour:00}:{minute:00}";
    }

    private static bool ContainsConfiguredAlias(FactSchemaEntry? entry, string message)
    {
        if (entry is null || entry.Aliases.Count == 0)
            return false;

        var normalizedMessage = NormalizeText(message);
        return entry.Aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(NormalizeText)
            .Any(alias => normalizedMessage.Contains(alias, StringComparison.Ordinal));
    }

    private static string NormalizeText(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
    }
}


