using FluentAssertions;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class PromptTemplateRendererTests
{
    private readonly PromptTemplateRenderer _renderer = new();

    [Fact]
    public void Render_ReplacesSimpleVariables()
    {
        const string template = "Servicio: {{service_name}} — Total: ${{total}}";
        var data = new Dictionary<string, object?>
        {
            ["service_name"] = "Plan Marineritos",
            ["total"] = "100,000"
        };

        var result = _renderer.Render(template, data);

        result.Should().Be("Servicio: Plan Marineritos — Total: $100,000");
    }

    [Fact]
    public void Render_ProcessesIfBlock()
    {
        const string template = """
            Base
            {{#if baby_name}}
            Bebe: {{baby_name}}
            {{/if}}
            Fin
            """;

        var withBaby = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["baby_name"] = "Thomas"
        });

        withBaby.Should().Contain("Bebe: Thomas");

        var withoutBaby = _renderer.Render(template, new Dictionary<string, object?>());
        withoutBaby.Should().NotContain("Bebe:");
        withoutBaby.Should().Contain("Base");
        withoutBaby.Should().NotContain("\n\n\n");
    }

    [Fact]
    public void Render_CheckoutTemplate_OmitsMissingOptionalFieldsWithoutBlankLines()
    {
        const string template = """
            - Nombre del cliente: {{customer_name}}
            - Telefono: {{customer_phone}}
            {{#if baby_age_months}}
            - Edad del bebe: {{baby_age_months}}
            {{/if}}
            {{#if baby_name}}
            - Nombre del bebe: {{baby_name}}
            {{/if}}

            💰 Anticipo
            """;

        var withoutOptional = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["customer_name"] = "Richard",
            ["customer_phone"] = "+1234567890"
        });

        withoutOptional.Should().NotContain("Edad del bebe");
        withoutOptional.Should().NotContain("Nombre del bebe");
        withoutOptional.Should().NotContain("{{#if");
        withoutOptional.Should().NotContain("{{/if}}");
        withoutOptional.Should().Contain("- Telefono: +1234567890");
        withoutOptional.Should().Contain("💰 Anticipo");
        withoutOptional.Split('\n').Count(l => string.IsNullOrWhiteSpace(l)).Should().BeLessOrEqualTo(1);

        var withOptional = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["customer_name"] = "Richard",
            ["customer_phone"] = "+1234567890",
            ["baby_age_months"] = "5",
            ["baby_name"] = "Thomas"
        });

        withOptional.Should().Contain("- Edad del bebe: 5");
        withOptional.Should().Contain("- Nombre del bebe: Thomas");
        withOptional.Should().NotContain("{{#if");
    }

    [Fact]
    public void Render_ProcessesEachBlock()
    {
        const string template = """
            {{#each addons}}
            - {{name}}: ${{price}}
            {{/each}}
            """;

        var data = new Dictionary<string, object?>
        {
            ["addons"] = new List<object>
            {
                new Dictionary<string, object?> { ["name"] = "Masaje", ["price"] = "30,000" }
            }
        };

        var result = _renderer.Render(template, data);
        result.Should().Contain("- Masaje: $30,000");
    }
}
