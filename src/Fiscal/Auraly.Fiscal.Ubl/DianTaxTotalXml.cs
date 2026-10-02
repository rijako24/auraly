using System.Globalization;
using System.Xml.Linq;

namespace Auraly.Fiscal.Ubl;

internal static class DianTaxTotalXml
{
    private static readonly XNamespace Cac = DianUblNamespaces.Cac;
    private static readonly XNamespace Cbc = DianUblNamespaces.Cbc;

    internal static void ValidateSupportWithholdings(IReadOnlyList<DianTax>? taxes)
    {
        if (taxes?.Any(tax => (tax.Code, tax.Name) is not (("05", "ReteIVA") or ("06", "ReteRenta")) ||
            tax.TaxableAmount < 0 || tax.Amount <= 0 || tax.Percent is <= 0 or > 100 ||
            tax.Percent != decimal.Round(tax.Percent, 3) ||
            decimal.Round(tax.TaxableAmount * tax.Percent / 100m, 4, MidpointRounding.AwayFromZero) != tax.Amount) == true)
            throw new ArgumentException("Support-document withholdings have invalid codes, bases or amounts.");
    }

    public static IEnumerable<XElement> Withholding(IReadOnlyList<DianTax>? taxes, string currency) =>
        Header(taxes ?? [], currency, "WithholdingTaxTotal", "0.00#");

    public static IEnumerable<XElement> Header(
        IEnumerable<DianTax> taxes,
        string currency, string totalElementName = "TaxTotal", string percentFormat = "0.00")
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

            yield return new XElement(Cac + totalElementName,
                MoneyElement("TaxAmount", taxType.Sum(tax => tax.Amount), currency),
                taxType.GroupBy(tax => tax.Percent)
                    .OrderBy(group => group.Key)
                    .Select(rate => TaxSubtotal(
                        taxType.Key,
                        names[0],
                        rate.Sum(tax => tax.TaxableAmount),
                        rate.Sum(tax => tax.Amount),
                        rate.Key,
                        currency, percentFormat)));
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
        string currency, string percentFormat) =>
        new(Cac + "TaxSubtotal",
            MoneyElement("TaxableAmount", taxableAmount, currency),
            MoneyElement("TaxAmount", amount, currency),
            new XElement(Cac + "TaxCategory",
                new XElement(Cbc + "Percent", percent.ToString(percentFormat, CultureInfo.InvariantCulture)),
                new XElement(Cac + "TaxScheme",
                    new XElement(Cbc + "ID", code),
                    new XElement(Cbc + "Name", name))));

    private static XElement MoneyElement(string name, decimal value, string currency) =>
        new(Cbc + name, new XAttribute("currencyID", currency), Money(value));

    private static string Money(decimal value) => DianUblAmountFormatter.Money(value);

}
