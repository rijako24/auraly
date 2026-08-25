using System.Globalization;
using System.Text;

namespace Auraly.Domain.Catalog;

public static class ProductAliasNormalizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "al", "con", "de", "del", "el", "en", "la", "las", "los", "o", "para", "por", "un", "una", "y", "x"
    };
    private static readonly HashSet<string> MeasurementUnits = new(StringComparer.Ordinal)
    {
        "g", "gr", "kg", "mg", "ml", "l", "cl", "cc", "oz", "lb", "und", "unidad", "unidades"
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousSpace = true;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }
        return string.Join(' ', builder.ToString().Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitAlphaNumeric)
            .Where(token => !StopWords.Contains(token))
            .Where(token => token.Length > 1 || token.All(char.IsDigit) || MeasurementUnits.Contains(token))
            .Select(Singularize)
            .Distinct(StringComparer.Ordinal));
    }

    private static string Singularize(string value)
    {
        if (value.Length > 4 && value.EndsWith("ces", StringComparison.Ordinal)) return $"{value[..^3]}z";
        if (value.Length > 4 && value.EndsWith("es", StringComparison.Ordinal)) return value[..^2];
        if (value.Length > 3 && value.EndsWith('s')) return value[..^1];
        return value;
    }

    private static IEnumerable<string> SplitAlphaNumeric(string token)
    {
        if (token.Length == 0) yield break;
        var start = 0;
        for (var index = 1; index < token.Length; index++)
        {
            if (char.IsDigit(token[index]) == char.IsDigit(token[index - 1])) continue;
            yield return token[start..index];
            start = index;
        }
        yield return token[start..];
    }
}
