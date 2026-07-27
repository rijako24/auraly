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
