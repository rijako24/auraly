namespace Auraly.Domain.Catalog;

public sealed record ScaleBarcodeRule(
    string Prefix,
    int ProductCodeStart,
    int ProductCodeLength,
    int ValueStart,
    int ValueLength,
    int DecimalPlaces,
    string EmbeddedValueType);

public sealed record ScaleBarcodeValue(string ProductCode, decimal Value, string EmbeddedValueType);

public static class ScaleBarcodeParser
{
    public static ScaleBarcodeValue Parse(string barcode, ScaleBarcodeRule rule)
    {
        if (string.IsNullOrWhiteSpace(barcode) || !barcode.StartsWith(rule.Prefix, StringComparison.Ordinal) ||
            barcode.Length < Math.Max(rule.ProductCodeStart + rule.ProductCodeLength, rule.ValueStart + rule.ValueLength))
            throw new FormatException("The barcode does not match the configured scale format.");
        var productCode = barcode.Substring(rule.ProductCodeStart, rule.ProductCodeLength);
        var raw = barcode.Substring(rule.ValueStart, rule.ValueLength);
        if (!long.TryParse(raw, out var value)) throw new FormatException("The embedded scale value is not numeric.");
        return new ScaleBarcodeValue(productCode, value / (decimal)Math.Pow(10, rule.DecimalPlaces), rule.EmbeddedValueType);
    }
}
