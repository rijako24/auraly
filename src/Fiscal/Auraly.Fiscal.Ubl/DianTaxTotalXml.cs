using System.Globalization;
using System.Xml.Linq;

namespace Auraly.Fiscal.Ubl;

internal static class DianTaxTotalXml
{
    private static readonly XNamespace Cac = DianUblNamespaces.Cac;
    private static readonly XNamespace Cbc = DianUblNamespaces.Cbc;

    public static IEnumerable<XElement> Header(
        IEnumerable<DianTax> taxes,
        string currency)
    {
        foreach (var taxType in taxes
                     .GroupBy(tax => tax.Code, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var names = taxType.Select(tax => tax.Name)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length != 1)
                throw new ArgumentException(
                    $"Tax code '{taxType.Key}' has inconsistent DIAN names.");

            yield return new XElement(Cac + "TaxTotal",
                MoneyElement("TaxAmount", taxType.Sum(tax => tax.Amount), currency),
                taxType.GroupBy(tax => tax.Percent)
                    .OrderBy(group => group.Key)
                    .Select(rate => TaxSubtotal(
                        taxType.Key,
                        names[0],
                        rate.Sum(tax => tax.TaxableAmount),
                        rate.Sum(tax => tax.Amount),
                        rate.Key,
                        currency)));
        }
    }

    public static IEnumerable<XElement> Line(
        IEnumerable<DianTax> taxes,
        string currency) =>
        Header(taxes, currency);

    private static XElement TaxSubtotal(
        string code,
        string name,
        decimal taxableAmount,
        decimal amount,
        decimal percent,
        string currency) =>
        new(Cac + "TaxSubtotal",
            MoneyElement("TaxableAmount", taxableAmount, currency),
            MoneyElement("TaxAmount", amount, currency),
            new XElement(Cac + "TaxCategory",
                new XElement(Cbc + "Percent", Number(percent)),
                new XElement(Cac + "TaxScheme",
                    new XElement(Cbc + "ID", code),
                    new XElement(Cbc + "Name", name))));

    private static XElement MoneyElement(string name, decimal value, string currency) =>
        new(Cbc + name, new XAttribute("currencyID", currency), Money(value));

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Number(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
