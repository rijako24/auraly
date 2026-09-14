using Auraly.Contracts.Sales;

namespace Auraly.Foundation.Tests;

public sealed class PosSaleFiscalMappingsTests
{
    [Theory]
    [InlineData("CC", "13")]
    [InlineData("CE", "22")]
    [InlineData("TI", "12")]
    [InlineData("NIT", "31")]
    [InlineData("PA", "41")]
    [InlineData("DE", "42")]
    [InlineData("PPT", "48")]
    [InlineData("13", "13")]
    public void Internal_identification_type_maps_to_dian_code(
        string source, string expected)
    {
        Assert.Equal(expected,
            PosSaleFiscalMappings.DianIdentificationTypeCode(source));
    }

    [Fact]
    public void Unsupported_identification_type_has_no_silent_fallback()
    {
        Assert.Null(PosSaleFiscalMappings.DianIdentificationTypeCode("UNKNOWN"));
    }
}
