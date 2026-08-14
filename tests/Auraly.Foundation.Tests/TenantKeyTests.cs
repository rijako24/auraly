using Auraly.BuildingBlocks.Domain.Identity;
using Xunit;

namespace Auraly.Foundation.Tests;

public sealed class TenantKeyTests
{
    [Theory]
    [InlineData("AURALY", "@auraly")]
    [InlineData("@Sj-Distribuciones", "@sj-distribuciones")]
    public void Parse_normalizes_to_an_at_prefixed_stable_key(
        string input,
        string expected)
    {
        Assert.Equal(expected, TenantKey.Parse(input).Value);
    }

    [Fact]
    public void From_name_creates_a_human_readable_key_without_accents()
    {
        Assert.Equal(
            "@sion-distribuciones",
            TenantKey.FromName("Sión Distribuciones").Value);
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@-empresa")]
    [InlineData("@empresa_1")]
    public void Invalid_keys_are_rejected(string input)
    {
        Assert.Throws<ArgumentException>(() => TenantKey.Parse(input));
    }
}
