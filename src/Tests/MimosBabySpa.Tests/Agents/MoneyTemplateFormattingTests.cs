using FluentAssertions;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class MoneyTemplateFormattingTests
{
    private readonly PromptTemplateRenderer _renderer = new();

    [Fact]
    public void FormatsNumericCatalogAndCartAmountsWithoutChangingQuantities()
    {
        const string template = """
            {{#each products}}
            - {{name}} x{{quantity}}: ${{unit_price}} {{currency}}
            {{/each}}
            Total: ${{total}} {{currency}}
            """;
        var data = new Dictionary<string, object?>
        {
            ["currency"] = "COP",
            ["total"] = 28661.50m,
            ["products"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "SALCHICHA RANCHERA",
                    ["quantity"] = 2m,
                    ["unit_price"] = 28661.50m,
                    ["currency"] = "COP"
                }
            }
        };

        var result = _renderer.Render(template, data);

        result.Should().Contain("SALCHICHA RANCHERA x2: $28,661.50 COP");
        result.Should().Contain("Total: $28,661.50 COP");
    }

    [Fact]
    public void KeepsAmountsAlreadyFormattedByAnAuthoritativeCheckoutOperation()
    {
        var result = _renderer.Render(
            "Total: ${{total}} {{currency}}",
            new Dictionary<string, object?> { ["total"] = "29,597", ["currency"] = "COP" });

        result.Should().Be("Total: $29,597 COP");
    }
}
