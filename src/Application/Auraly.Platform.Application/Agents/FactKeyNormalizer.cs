using System.Text;
using System.Text.RegularExpressions;

namespace Auraly.Platform.Application.Agents;

/// <summary>
/// Normaliza claves de hechos a snake_case para consistencia entre turnos.
/// </summary>
public static partial class FactKeyNormalizer
{
    private const int MaxKeyLength = 64;
    private const int MaxValueLength = 500;

    public static bool TryNormalizeKey(string? rawKey, out string normalizedKey)
    {
        normalizedKey = string.Empty;
        if (string.IsNullOrWhiteSpace(rawKey))
            return false;

        var trimmed = rawKey.Trim();
        var snake = ToSnakeCase(trimmed);
        snake = InvalidKeyChars().Replace(snake, string.Empty);
        snake = MultiUnderscore().Replace(snake, "_").Trim('_');

        if (string.IsNullOrWhiteSpace(snake) || snake.Length > MaxKeyLength)
            return false;

        normalizedKey = snake;
        return true;
    }

    public static bool TryNormalizeValue(string? rawValue, out string normalizedValue)
    {
        normalizedValue = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        var trimmed = rawValue.Trim();
        if (trimmed.Length > MaxValueLength)
            trimmed = trimmed[..MaxValueLength];

        normalizedValue = trimmed;
        return true;
    }

    private static string ToSnakeCase(string input)
    {
        var sb = new StringBuilder(input.Length + 8);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c) || c is '-' or '.')
            {
                sb.Append('_');
                continue;
            }

            if (char.IsUpper(c) && i > 0 && input[i - 1] != '_')
                sb.Append('_');

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"[^a-z0-9_]")]
    private static partial Regex InvalidKeyChars();

    [GeneratedRegex(@"_+")]
    private static partial Regex MultiUnderscore();
}
