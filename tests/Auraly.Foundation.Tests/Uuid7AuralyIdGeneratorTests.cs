using Auraly.BuildingBlocks.Infrastructure.Identifiers;

namespace Auraly.Foundation.Tests;

public sealed class Uuid7AuralyIdGeneratorTests
{
    [Fact]
    public void NewId_creates_non_empty_rfc_9562_version_7_identifiers()
    {
        var generator = new Uuid7AuralyIdGenerator(TimeProvider.System);

        var value = generator.NewId();
        var text = value.ToString("D");

        Assert.NotEqual(Guid.Empty, value);
        Assert.Equal('7', text[14]);
        Assert.Contains(text[19], new[] { '8', '9', 'a', 'b' });
    }

    [Fact]
    public void NewId_does_not_reveal_a_sequential_business_counter()
    {
        var generator = new Uuid7AuralyIdGenerator(TimeProvider.System);

        var values = Enumerable.Range(0, 1_000).Select(_ => generator.NewId()).ToArray();

        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
