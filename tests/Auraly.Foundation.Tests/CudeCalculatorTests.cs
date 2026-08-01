using Auraly.Fiscal.Core;

namespace Auraly.Foundation.Tests;

public sealed class CudeCalculatorTests
{
    [Fact]
    public void Official_annex_vector_matches_sha384_cude()
    {
        var input = new CudeInput(
            "8110007871",
            new DateTimeOffset(2019, 1, 12, 7, 0, 0, TimeSpan.FromHours(-5)),
            5000.00m,
            5950.00m,
            "900373076",
            "8355990",
            "12301",
            FiscalEnvironment.Production,
            [new FiscalTaxAmount("01", 950m)]);

        var result = CudeCalculator.Calculate(input, "https://catalogo-vpfe.dian.gov.co/document/searchqr");

        Assert.Equal(
            "907e4444decc9e59c160a2fb3b6659b33dc5b632a5008922b9a62f83f757b1c448e47f5867f2b50dbdb96f48c7681168",
            result.Cude);
        Assert.Contains($"CUDE: {result.Cude}", result.QrPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_value_uses_fixed_tax_order_zeroes_and_truncation()
    {
        var input = new CudeInput(
            "NC1",
            new DateTimeOffset(2026, 8, 1, 12, 13, 14, TimeSpan.FromHours(-5)),
            10.999m,
            12.999m,
            "9001",
            "2222",
            "PIN",
            FiscalEnvironment.Test,
            [new FiscalTaxAmount("03", 2.999m)]);

        Assert.Equal(
            "NC12026-08-0112:13:14-05:0010.99010.00040.00032.9912.9990012222PIN2",
            CudeCalculator.BuildCanonicalValue(input));
    }
}
