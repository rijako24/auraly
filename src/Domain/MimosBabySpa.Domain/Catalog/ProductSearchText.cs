using System.Globalization;
using System.Text;

namespace MimosBabySpa.Domain.Catalog;

public static class ProductSearchText
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "al", "con", "de", "del", "el", "en", "la", "las", "los", "o", "para", "por", "un", "una", "y", "x"
    };

    private static readonly string[] DerivationalSuffixes =
    [
        "adas", "ados", "idas", "idos", "ando", "iendo", "ada", "ado", "ida", "ido"
    ];

    public static string NormalizeAlias(string? value) =>
        string.Join(' ', GetTokens(value));

    public static IReadOnlyList<string> GetTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return NormalizeWords(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitAlphaNumeric)
            .Where(term => !StopWords.Contains(term))
            .Where(term => term.Length > 1 || term.All(char.IsDigit))
            .Select(Singularize)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyCollection<string> GetSearchKeys(string? value)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in GetTokens(value))
        {
            AddTokenKeys(keys, token);
            foreach (var stem in GetStems(token))
                AddTokenKeys(keys, stem);
        }
        return keys;
    }

    public static IReadOnlyCollection<string> GetIndexTerms(params string?[] values)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            foreach (var token in GetTokens(value))
            {
                AddTokenKeys(terms, token);
                foreach (var stem in GetStems(token))
                    AddTokenKeys(terms, stem);
            }
        }
        return terms;
    }

    public static double TokenSimilarity(string left, string right)
    {
        if (left.Equals(right, StringComparison.Ordinal))
            return 1d;
        if (GetStems(left).Intersect(GetStems(right), StringComparer.Ordinal).Any())
            return 0.94d;

        var prefix = CommonPrefixLength(left, right);
        var prefixScore = prefix >= 4
            ? Math.Min(0.82d, 0.62d + prefix * 0.04d)
            : 0d;
        var dice = NGramDice(left, right, 3);
        var edit = NormalizedLevenshtein(left, right);
        return Math.Max(prefixScore, Math.Max(dice * 0.9d, edit * 0.82d));
    }

    public static string NormalizeWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousSpace = true;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
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
        return builder.ToString().Trim();
    }

    private static void AddTokenKeys(ISet<string> keys, string token)
    {
        if (token.Length == 0 || token.Length > 100)
            return;
        keys.Add(token);
        if (token.Length >= 5 && token.All(char.IsLetter))
            keys.Add(token[..4]);
    }

    private static IEnumerable<string> GetStems(string token)
    {
        yield return token;
        if (!token.All(char.IsLetter))
            yield break;

        foreach (var suffix in DerivationalSuffixes)
        {
            if (token.Length - suffix.Length >= 4 && token.EndsWith(suffix, StringComparison.Ordinal))
                yield return token[..^suffix.Length];
        }
    }

    private static string Singularize(string value)
    {
        if (value.Length > 4 && value.EndsWith("ces", StringComparison.Ordinal))
            return $"{value[..^3]}z";
        if (value.Length > 4 && value.EndsWith("es", StringComparison.Ordinal))
            return value[..^2];
        if (value.Length > 3 && value.EndsWith('s'))
            return value[..^1];
        return value;
    }

    private static IEnumerable<string> SplitAlphaNumeric(string token)
    {
        if (token.Length == 0)
            yield break;
        var start = 0;
        for (var index = 1; index < token.Length; index++)
        {
            if (char.IsDigit(token[index]) == char.IsDigit(token[index - 1]))
                continue;
            yield return token[start..index];
            start = index;
        }
        yield return token[start..];
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
            index++;
        return index;
    }

    private static double NGramDice(string left, string right, int size)
    {
        if (left.Length < size || right.Length < size)
            return 0d;
        var leftGrams = Enumerable.Range(0, left.Length - size + 1).Select(i => left.Substring(i, size)).ToHashSet(StringComparer.Ordinal);
        var rightGrams = Enumerable.Range(0, right.Length - size + 1).Select(i => right.Substring(i, size)).ToHashSet(StringComparer.Ordinal);
        return 2d * leftGrams.Intersect(rightGrams, StringComparer.Ordinal).Count() / (leftGrams.Count + rightGrams.Count);
    }

    private static double NormalizedLevenshtein(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return 0d;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return 1d - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }
}
