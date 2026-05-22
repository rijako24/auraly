using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class AgentTurnResponseComposerTests
{
    [Fact]
    public void Compose_ReplacesTokenInLlmResponse()
    {
        const string prompt = """
            [template: checkout_no_deposit]
            ```
            TOTAL: ${{total}}
            ```
            """;

        var composer = new AgentTurnResponseComposer(
            new PromptTemplateExtractor(),
            new PromptTemplateRenderer(),
            NullLogger<AgentTurnResponseComposer>.Instance);

        const string token = "{{CHECKOUT:abc123}}";
        var fragments = new[]
        {
            new TurnFragmentEntry(token, new TurnFragment(
                "checkout_no_deposit",
                new Dictionary<string, object?> { ["total"] = "100,000" }))
        };

        var result = composer.Compose(prompt, $"Gracias! {token}", fragments);

        result.Should().Contain("TOTAL: $100,000");
        result.Should().NotContain(token);
    }

    [Fact]
    public void Compose_ExclusiveMode_DiscardsLlmProse()
    {
        const string prompt = """
            [template: checkout_with_deposit]
            ```
            TOTAL: ${{total}}
            ```
            """;

        var composer = new AgentTurnResponseComposer(
            new PromptTemplateExtractor(),
            new PromptTemplateRenderer(),
            NullLogger<AgentTurnResponseComposer>.Instance);

        const string token = "{{CHECKOUT:abc123}}";
        var fragments = new[]
        {
            new TurnFragmentEntry(token, new TurnFragment(
                "checkout_with_deposit",
                new Dictionary<string, object?> { ["total"] = "100,000" },
                FragmentRenderMode.Exclusive))
        };

        var result = composer.Compose(
            prompt,
            "Aquí tienes el resumen con anticipo del 50%. Por favor paga en el enlace.",
            fragments);

        result.Should().Be("TOTAL: $100,000");
        result.Should().NotContain("Aquí tienes");
        result.Should().NotContain(token);
    }

    [Fact]
    public void Compose_PrependsWhenTokenMissing()
    {
        const string prompt = """
            [template: checkout_no_deposit]
            ```
            TOTAL: ${{total}}
            ```
            """;

        var composer = new AgentTurnResponseComposer(
            new PromptTemplateExtractor(),
            new PromptTemplateRenderer(),
            NullLogger<AgentTurnResponseComposer>.Instance);

        const string token = "{{CHECKOUT:xyz789}}";
        var fragments = new[]
        {
            new TurnFragmentEntry(token, new TurnFragment(
                "checkout_no_deposit",
                new Dictionary<string, object?> { ["total"] = "50,000" }))
        };

        var result = composer.Compose(prompt, "¿Confirmas?", fragments);

        result.Should().StartWith("TOTAL: $50,000");
        result.Should().EndWith("¿Confirmas?");
    }
}
