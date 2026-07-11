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
            _ => entry?.Aliases.Count > 0 && ContainsConfiguredAlias(entry, message)
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
            .Any(alias => normalizedMessage.Contains(alias, StringComparison.Ordinal)
                || FuzzyTokenMatches(normalizedMessage, alias));
    }

    private static bool FuzzyTokenMatches(string normalizedMessage, string normalizedAlias)
    {
        var messageTokens = SplitTokens(normalizedMessage);
        var aliasTokens = SplitTokens(normalizedAlias);
        if (messageTokens.Length == 0 || aliasTokens.Length == 0)
            return false;

        return aliasTokens.All(aliasToken =>
            messageTokens.Any(messageToken => IsNearToken(messageToken, aliasToken)));
    }

    private static string[] SplitTokens(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsNearToken(string left, string right)
    {
        if (left.Equals(right, StringComparison.Ordinal))
            return true;

        const int MinimumFuzzyLength = 5;
        if (left.Length < MinimumFuzzyLength || right.Length < MinimumFuzzyLength)
            return false;

        if (Math.Abs(left.Length - right.Length) > 1)
            return false;

        return LevenshteinDistance(left, right) <= 1;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var index = 0; index <= right.Length; index++)
            previous[index] = index;

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
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

