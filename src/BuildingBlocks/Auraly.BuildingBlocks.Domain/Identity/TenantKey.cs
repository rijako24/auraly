using System.Globalization;
using System.Text;

namespace Auraly.BuildingBlocks.Domain.Identity;

public readonly record struct TenantKey
{
    public const int MaximumLength = 64;
    public string Value { get; }

    private TenantKey(string value) => Value = value;

    public static TenantKey Parse(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('@')) normalized = $"@{normalized}";
        if (normalized.Length is < 3 or > MaximumLength ||
            !char.IsLetterOrDigit(normalized[1]) ||
            normalized.Skip(2).Any(character =>
                !char.IsLetterOrDigit(character) && character != '-'))
            throw new ArgumentException(
                "La clave de empresa debe iniciar con @ y contener solo letras, números o guiones.");
        return new TenantKey(normalized);
    }

    public static TenantKey FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre de la empresa es obligatorio.");

        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var pendingSeparator = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) ==
                UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0) builder.Append('-');
                builder.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else pendingSeparator = builder.Length > 0;
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0) slug = "empresa";
        if (slug.Length > MaximumLength - 1) slug = slug[..(MaximumLength - 1)].TrimEnd('-');
        return Parse($"@{slug}");
    }

    public override string ToString() => Value;
}
