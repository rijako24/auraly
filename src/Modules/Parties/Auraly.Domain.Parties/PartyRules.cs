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
            throw new ArgumentException("Identification type is required.", nameof(identificationTypeCode));
        if (string.IsNullOrWhiteSpace(identification))
            throw new ArgumentException("Identification is required.", nameof(identification));

        var value = identification.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        value = CompactDocumentTypes.Contains(identificationTypeCode.Trim())
            ? string.Concat(value.Where(char.IsLetterOrDigit))
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (value.Length is < 3 or > 64)
            throw new ArgumentOutOfRangeException(
                nameof(identification), "Normalized identification must contain between 3 and 64 characters.");
        return value;
    }
}

public sealed class CustomerPricingAssignment
{
    public CustomerPricingAssignment(Guid? priceListId, Guid? priceChannelId)
    {
        if (priceListId.HasValue && priceChannelId.HasValue)
            throw new ArgumentException("A customer can use a price list or a price channel, never both.");
        if (priceListId == Guid.Empty || priceChannelId == Guid.Empty)
            throw new ArgumentException("Pricing identifiers cannot be empty.");
        PriceListId = priceListId;
        PriceChannelId = priceChannelId;
    }

    public Guid? PriceListId { get; }
    public Guid? PriceChannelId { get; }
}

public static class PartyValidation
{
    public static void RequireText(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{field} is required.", field);
        if (value.Trim().Length > maximumLength)
            throw new ArgumentOutOfRangeException(field, $"{field} cannot exceed {maximumLength} characters.");
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
            throw new ArgumentException($"{field} contains unsupported characters.", field);
        return normalized;
    }
}
