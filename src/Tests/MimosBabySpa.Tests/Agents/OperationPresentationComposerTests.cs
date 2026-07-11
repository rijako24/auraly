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
}
