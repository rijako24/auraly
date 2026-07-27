using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Auraly.Fiscal.Core;

public sealed record CufeResult(string Cufe, string QrPayload);

public static class CufeCalculator
{
    private static readonly string[] DianTaxOrder = ["01", "04", "03"];

    public static CufeResult Calculate(CufeInput input, string qrBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(qrBaseUrl))
        {
            throw new ArgumentException("A QR validation URL is required.", nameof(qrBaseUrl));
        }

        var canonicalValue = BuildCanonicalValue(input);
        var hash = SHA384.HashData(Encoding.UTF8.GetBytes(canonicalValue));
        var cufe = Convert.ToHexString(hash).ToLowerInvariant();
        return new CufeResult(cufe, BuildQrPayload(input, cufe, qrBaseUrl));
    }

    public static string BuildCanonicalValue(CufeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var taxes = input.Taxes
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(value => value.Amount), StringComparer.Ordinal);

        var builder = new StringBuilder()
            .Append(input.InvoiceNumber)
            .Append(input.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(input.IssuedAt.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture))
            .Append(Money(input.UntaxedAmount));

        foreach (var taxCode in DianTaxOrder)
        {
            builder.Append(taxCode);
            builder.Append(Money(taxes.GetValueOrDefault(taxCode)));
        }

        builder
            .Append(Money(input.PayableAmount))
            .Append(input.SupplierTaxId)
            .Append(input.CustomerIdentification)
            .Append(input.TechnicalKey.Reveal())
            .Append(((int)input.Environment).ToString(CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    private static string BuildQrPayload(CufeInput input, string cufe, string qrBaseUrl)
    {
        var taxes = input.Taxes
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(value => value.Amount), StringComparer.Ordinal);

        var otherTaxes = taxes
            .Where(x => x.Key != "01")
            .Sum(x => x.Value);

        return string.Join(
            "\n",
            $"NumFac: {input.InvoiceNumber}",
            $"FecFac: {input.IssuedAt:yyyy-MM-dd}",
            $"HorFac: {input.IssuedAt:HH:mm:sszzz}",
            $"NitFac: {input.SupplierTaxId}",
            $"DocAdq: {input.CustomerIdentification}",
            $"ValFac: {Money(input.UntaxedAmount)}",
            $"ValIva: {Money(taxes.GetValueOrDefault("01"))}",
            $"ValOtroIm: {Money(otherTaxes)}",
            $"ValTolFac: {Money(input.PayableAmount)}",
            $"CUFE: {cufe}",
            $"{qrBaseUrl.TrimEnd('/')}?documentkey={cufe}");
    }

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
