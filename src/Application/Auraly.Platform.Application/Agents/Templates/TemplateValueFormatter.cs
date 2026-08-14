using System.Globalization;

namespace Auraly.Platform.Application.Agents.Templates;

internal static class TemplateValueFormatter
{
    public static string Format(string fieldName, object? value)
    {
        if (value is null)
            return string.Empty;
        if (value is string text)
            return text;
        if (IsMoneyField(fieldName) && TryDecimal(value, out var amount))
            return amount.ToString("N2", CultureInfo.InvariantCulture);
        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }

    private static bool IsMoneyField(string fieldName)
    {
        var normalized = fieldName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized is "price"
            or "unitprice"
            or "linetotal"
            or "subtotal"
            or "discount"
            or "discountamount"
            or "tax"
            or "taxamount"
            or "shippingcost"
            or "total"
            or "amount";
    }

    private static bool TryDecimal(object value, out decimal amount)
    {
        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                amount = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            default:
                amount = 0;
                return false;
        }
    }
}
