using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Auraly.Fiscal.Core;

public sealed record CudsInput(
    string DocumentNumber,
    DateTimeOffset IssuedAt,
    decimal UntaxedAmount,
    decimal VatAmount,
    decimal PayableAmount,
    string SellerIdentification,
    string BuyerTaxId,
    string SoftwarePin,
    FiscalEnvironment Environment);

public sealed record CudsResult(string Cuds, string QrPayload);

public static class CudsCalculator
{
    public static CudsResult Calculate(CudsInput input, string qrBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(qrBaseUrl))
            throw new ArgumentException("A QR validation URL is required.", nameof(qrBaseUrl));
        var canonical = string.Concat(
            input.DocumentNumber,
            input.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            input.IssuedAt.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture),
            Money(input.UntaxedAmount), "01", Money(input.VatAmount),
            Money(input.PayableAmount), input.SellerIdentification,
            input.BuyerTaxId, input.SoftwarePin,
            ((int)input.Environment).ToString(CultureInfo.InvariantCulture));
        var cuds = Convert.ToHexString(
            SHA384.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var qr = string.Join("\n",
            $"NumDS: {input.DocumentNumber}",
            $"FecDS: {input.IssuedAt:yyyy-MM-dd}",
            $"HorDS: {input.IssuedAt:HH:mm:sszzz}",
            $"NumSNO: {input.SellerIdentification}",
            $"DocAdq: {input.BuyerTaxId}",
            $"ValDS: {Money(input.UntaxedAmount)}",
            $"ValIva: {Money(input.VatAmount)}",
            $"ValTolDS: {Money(input.PayableAmount)}",
            $"CUDS: {cuds}",
            $"{qrBaseUrl.TrimEnd('/')}?documentkey={cuds}");
        return new(cuds, qr);
    }

    private static string Money(decimal value) =>
        (decimal.Truncate(value * 100m) / 100m)
            .ToString("0.00", CultureInfo.InvariantCulture);
}
