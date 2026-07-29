using Auraly.Fiscal.Core;

namespace Auraly.Foundation.Tests;

public sealed class CufeCalculatorTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 7, 27, 14, 35, 12, TimeSpan.FromHours(-5));

    [Fact]
    public void Canonical_value_has_dian_order_and_invariant_decimal_format()
    {
        var input = CreateInput("FV01123", 100_000m, 119_000m);

        var canonical = CufeCalculator.BuildCanonicalValue(input);

        Assert.Equal(
            "FV011232026-07-2714:35:12-05:00100000.000119000.00040.00030.00119000.009001234567222222222CLAVE-TECNICA2",
            canonical);
    }

    [Fact]
    public void Same_snapshot_produces_same_cufe_and_qr()
    {
        var input = CreateInput("FV01123", 100_000m, 119_000m);

        var edge = CufeCalculator.Calculate(input, "https://catalogo-vpfe.dian.gov.co/document/searchqr");
        var server = CufeCalculator.Calculate(input, "https://catalogo-vpfe.dian.gov.co/document/searchqr");

        Assert.Equal(edge, server);
        Assert.Equal(96, edge.Cufe.Length);
        Assert.Contains(edge.Cufe, edge.QrPayload);
    }

    [Fact]
    public void Official_dian_invoice_vector_produces_expected_cufe()
    {
        var input = new CufeInput(
            "323200000129",
            new DateTimeOffset(2019, 1, 16, 10, 53, 10, TimeSpan.FromHours(-5)),
            1_500_000m,
            1_785_000m,
            "700085371",
            "800199436",
            new FiscalTechnicalKey(
                "693ff6f2a553c3646a063436fd4dd9ded0311471",
                "official-fev-1.9"),
            FiscalEnvironment.Production,
            [new FiscalTaxAmount("01", 285_000m)]);

        var result = CufeCalculator.Calculate(
            input,
            "https://catalogo-vpfe.dian.gov.co/document/searchqr");

        Assert.Equal(
            "8bb918b19ba22a694f1da11c643b5e9de39adf60311cf179179e9b33381030bcd4c3c3f156c506ed5908f9276f5bd9b4",
            result.Cufe);
    }

    [Fact]
    public void Monetary_components_are_truncated_to_two_decimals()
    {
        var input = CreateInput("FV01123", 100.999m, 119.999m);

        var canonical = CufeCalculator.BuildCanonicalValue(input);

        Assert.Contains("100.99", canonical, StringComparison.Ordinal);
        Assert.Contains("119.99", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("101.00", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("120.00", canonical, StringComparison.Ordinal);
    }
    [Fact]
    public void Fiscal_change_produces_a_different_cufe()
    {
        var original = CufeCalculator.Calculate(
            CreateInput("FV01123", 100_000m, 119_000m),
            "https://catalogo-vpfe.dian.gov.co/document/searchqr");
        var changed = CufeCalculator.Calculate(
            CreateInput("FV01123", 100_001m, 119_001m),
            "https://catalogo-vpfe.dian.gov.co/document/searchqr");

        Assert.NotEqual(original.Cufe, changed.Cufe);
    }

    [Fact]
    public void Technical_key_never_appears_in_default_text_representation()
    {
        var key = new FiscalTechnicalKey("secret-value", "v1");

        Assert.DoesNotContain("secret-value", key.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", key.ToString(), StringComparison.Ordinal);
    }

    private static CufeInput CreateInput(string number, decimal untaxed, decimal payable) =>
        new(
            number,
            IssuedAt,
            untaxed,
            payable,
            "9001234567",
            "222222222",
            new FiscalTechnicalKey("CLAVE-TECNICA", "v1"),
            FiscalEnvironment.Test,
            [new FiscalTaxAmount("01", 19_000m)]);
}
