using System.Globalization;

namespace Auraly.Fiscal.Ubl;

internal static class DianUblAmountFormatter
{
    public static string Money(decimal value) =>
        value.ToString("0.00####", CultureInfo.InvariantCulture);
}
