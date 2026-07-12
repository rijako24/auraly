using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class OperationPresentationComposerTests
{
    [Fact]
    public void ExclusivePresentation_DiscardsLlmResponseAndRendersOnlyTemplate()
    {
        var composer = new OperationPresentationComposer(
            new AgentTemplateResolver(),
            new PromptTemplateRenderer());
        var config = new AgentConfig
        {
            Templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["availability_slots"] = "Espacios para {{service_name}}: {{#each options}}{{this}} {{/each}}"
            }
        };
        var presentation = new OperationPresentation(
            "availability_slots",
            new Dictionary<string, object?>
            {
                ["service_name"] = "Corte infantil",
                ["options"] = new List<object> { "9:00 AM", "10:00 AM" }
            },
            FragmentRenderMode.Exclusive,
            FragmentPriority.Required);

        var response = composer.Compose(
            config,
            "Claro, estos son los horarios que encontré:",
            [presentation]);

        response.Should().Be($"Espacios para Corte infantil: 9:00 AM{Environment.NewLine}10:00 AM");
        response.Should().NotContain("Claro");
    }
    [Fact]
    public void CatalogPresentation_RendersEveryReturnedProductWithItsAuthoritativePrice()
    {
        var composer = new OperationPresentationComposer(
            new AgentTemplateResolver(),
            new PromptTemplateRenderer());
        var config = new AgentConfig
        {
            Templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["catalog_results"] = "{{#each products}}- {{name}}: ${{unit_price}} {{currency}}{{/each}}"
            }
        };
        var presentation = new OperationPresentation(
            "catalog_results",
            new Dictionary<string, object?>
            {
                ["products"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Pollo",
                        ["unit_price"] = 12000m,
                        ["currency"] = "COP"
                    },
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Tocineta",
                        ["unit_price"] = 8500m,
                        ["currency"] = "COP"
                    }
                }
            },
            FragmentRenderMode.Exclusive,
            FragmentPriority.Required);

        var response = composer.Compose(config, "Precios estimados por el modelo", [presentation]);

        response.Should().Contain("Pollo: $12,000.00 COP");
        response.Should().Contain("Tocineta: $8,500.00 COP");
        response.Should().NotContain("estimados");
    }
}
