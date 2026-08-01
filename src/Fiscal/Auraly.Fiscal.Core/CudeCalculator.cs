using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Auraly.Fiscal.Core;

public sealed record CudeResult(string Cude, string QrPayload);

public static class CudeCalculator
{
    private static readonly string[] DianTaxOrder = ["01", "04", "03"];

    public static CudeResult Calculate(CudeInput input, string qrBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(qrBaseUrl))
            throw new ArgumentException("A QR validation URL is required.", nameof(qrBaseUrl));
        var canonical = BuildCanonicalValue(input);
        var hash = SHA384.HashData(Encoding.UTF8.GetBytes(canonical));
        var cude = Convert.ToHexString(hash).ToLowerInvariant();
        return new CudeResult(cude, BuildQrPayload(input, cude, qrBaseUrl));
    }

    public static string BuildCanonicalValue(CudeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var taxes = input.Taxes
            .GroupBy(value => value.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Amount), StringComparer.Ordinal);
        var value = new StringBuilder()
            .Append(input.CreditNoteNumber)
            .Append(input.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(input.IssuedAt.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture))
            .Append(Money(input.LineExtensionAmount));
        foreach (var code in DianTaxOrder)
            value.Append(code).Append(Money(taxes.GetValueOrDefault(code)));
        return value
            .Append(Money(input.PayableAmount))
            .Append(input.SupplierTaxId)
            .Append(input.CustomerIdentification)
            .Append(input.SoftwarePin)
            .Append(((int)input.Environment).ToString(CultureInfo.InvariantCulture))
            .ToString();
    }

    private static string BuildQrPayload(CudeInput input, string cude, string qrBaseUrl)
    {
        var taxes = input.Taxes
            .GroupBy(value => value.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Amount), StringComparer.Ordinal);
        return string.Join("\n",
            $"NumFac: {input.CreditNoteNumber}",
            $"FecFac: {input.IssuedAt:yyyy-MM-dd}",
            $"HorFac: {input.IssuedAt:HH:mm:sszzz}",
            $"NitFac: {input.SupplierTaxId}",
            $"DocAdq: {input.CustomerIdentification}",
            $"ValFac: {Money(input.LineExtensionAmount)}",
            $"ValIva: {Money(taxes.GetValueOrDefault("01"))}",
            $"ValOtroIm: {Money(taxes.Where(value => value.Key != "01").Sum(value => value.Value))}",
            $"ValTolFac: {Money(input.PayableAmount)}",
            $"CUDE: {cude}",
            $"{qrBaseUrl.TrimEnd('/')}?documentkey={cude}");
    }

    private static string Money(decimal value)
    {
        var truncated = decimal.Truncate(value * 100m) / 100m;
        return truncated.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
