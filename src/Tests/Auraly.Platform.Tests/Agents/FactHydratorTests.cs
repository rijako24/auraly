using FluentAssertions;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Facts;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class FactHydratorTests
{
    [Fact]
    public void Hydrate_UsesDefaultValueWhenNonUserFactIsMissing()
    {
        var hydrator = new FactHydrator([]);
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        hydrator.Hydrate(
            [
                new FactSchemaEntry
                {
                    Key = "city",
                    Source = "system",
                    DefaultValue = "Valledupar"
                }
            ],
            facts,
            new FactHydratorContext());

        facts.Should().Contain("city", "Valledupar");
    }

    [Fact]
    public void Hydrate_DoesNotOverwriteExistingFactWithDefaultValue()
    {
        var hydrator = new FactHydrator([]);
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["city"] = "Bogota"
        };

        hydrator.Hydrate(
            [
                new FactSchemaEntry
                {
                    Key = "city",
                    Source = "system",
                    DefaultValue = "Valledupar"
                }
            ],
            facts,
            new FactHydratorContext());

        facts.Should().Contain("city", "Bogota");
    }
}
