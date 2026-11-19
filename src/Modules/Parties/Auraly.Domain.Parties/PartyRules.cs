using System.Globalization;
using System.Text;

namespace Auraly.Domain.Parties;

public static class PartyIdentityNormalizer
{
    private static readonly HashSet<string> CompactDocumentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "NIT", "CC", "CE", "TI", "NUIP", "PPT" };

    public static string Normalize(string identificationTypeCode, string identification)
    {
        if (string.IsNullOrWhiteSpace(identificationTypeCode))
            throw new ArgumentException("Selecciona el tipo de identificación.");
        if (string.IsNullOrWhiteSpace(identification))
            throw new ArgumentException("Escribe la identificación.");

        var value = identification.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        value = CompactDocumentTypes.Contains(identificationTypeCode.Trim())
            ? string.Concat(value.Where(char.IsLetterOrDigit))
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (value.Length is < 3 or > 64)
            throw new ArgumentOutOfRangeException(
                null, "La identificación normalizada debe tener entre 3 y 64 caracteres.");
        return value;
    }
}

public sealed class CustomerPricingAssignment
{
    public CustomerPricingAssignment(Guid? priceChannelId)
    {
        if (priceChannelId == Guid.Empty)
            throw new ArgumentException("La identificación del canal de precios no puede estar vacía.");
        PriceChannelId = priceChannelId;
    }

    public Guid? PriceChannelId { get; }
}

public static class PartyValidation
{
    public static void RequireText(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"El campo {field} es obligatorio.");
        if (value.Trim().Length > maximumLength)
            throw new ArgumentOutOfRangeException(null, $"El campo {field} admite máximo {maximumLength} caracteres.");
    }

    public static string NormalizeCode(string value, string field, int maximumLength)
    {
        RequireText(value, field, maximumLength);
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var normalized = string.Concat(
            decomposed.Where(character =>
                CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
            .ToUpperInvariant();
        if (normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException($"El campo {field} contiene caracteres no permitidos.");
        return normalized;
    }
}
