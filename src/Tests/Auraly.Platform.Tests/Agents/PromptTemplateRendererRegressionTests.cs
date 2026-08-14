using FluentAssertions;
using Auraly.Platform.Application.Agents.Templates;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class PromptTemplateRendererRegressionTests
{
    [Fact]
    public void Render_ConvertsEscapedTemplateNewlinesWithoutTouchingVariableValues()
    {
        var renderer = new PromptTemplateRenderer();
        var template = @"Primera\r\n\r\nSegunda: {{value}}";
        var data = new Dictionary<string, object?>
        {
            ["value"] = @"conservar\r\nliteral"
        };

        var result = renderer.Render(template, data);

        result.Should().Be(
            $"Primera{Environment.NewLine}{Environment.NewLine}Segunda: conservar\\r\\nliteral");
        result.Should().NotContain(@"Primera\r\n");
    }

    [Fact]
    public void Render_NestedPendingFollowUp_ShowsOnlyAppliedChangesAndOneQuestion()
    {
        var renderer = new PromptTemplateRenderer();
        const string template = """
            {{#if is_pending_follow_up}}
            Listo, apliqué estos cambios al pedido:
            {{#each display_applied_items}}
            {{#if removed}}- Retiré {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} del carrito{{else}}- Agregué o actualicé {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}{{/if}}
            {{/each}}

            {{#if can_finalize_with_pending}}Si eso es todo, dejaré fuera las referencias sin existencia o sin coincidencia segura. ¿Eso sería todo o deseas agregar algo más?{{else}}¿Deseas agregar algo mas?{{/if}}
            {{else}}
            Procesé cada producto de tu solicitud:
            {{#if unavailable_items}}
            *Sin existencia*
            {{#each unavailable_items}}
            - {{description}}
            {{/each}}
            {{/if}}
            {{#if not_found_items}}
            *No encontrados*
            {{#each not_found_items}}
            - {{product_text}}
            {{/each}}
            {{/if}}
            {{#if can_finalize_with_pending}}Si eso es todo, dejaré fuera las referencias sin existencia o sin coincidencia segura. ¿Eso sería todo o deseas agregar algo más?{{else}}Indícame una referencia más precisa.{{/if}}
            {{/if}}
            """;
        var data = new Dictionary<string, object?>
        {
            ["is_pending_follow_up"] = true,
            ["display_applied_items"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["removed"] = true,
                    ["requested_name"] = "maíz",
                    ["name"] = "MAIZ CONGELADO 1 K",
                    ["quantity"] = null
                },
                new Dictionary<string, object?>
                {
                    ["removed"] = false,
                    ["requested_name"] = "jamonada CUNICHEF",
                    ["name"] = "JAMON CUNIT X 500GR",
                    ["quantity"] = 7m
                }
            },
            ["unavailable_items"] = new List<object>
            {
                new Dictionary<string, object?> { ["description"] = "chorizo — sin existencia" }
            },
            ["not_found_items"] = new List<object>
            {
                new Dictionary<string, object?> { ["product_text"] = "ranchera" }
            },
            ["can_finalize_with_pending"] = true
        };

        var result = renderer.Render(template, data);

        result.Should().Contain("- Retiré maíz (MAIZ CONGELADO 1 K) del carrito");
        result.Should().Contain("- Agregué o actualicé jamonada CUNICHEF (JAMON CUNIT X 500GR) — cantidad: 7");
        result.Should().Contain("dejaré fuera las referencias sin existencia o sin coincidencia segura");
        result.Split("dejaré fuera", StringSplitOptions.None).Should().HaveCount(2);
        result.Should().NotContain("\n¿Deseas agregar algo mas?");
        result.Should().NotContain("Sin existencia");
        result.Should().NotContain("No encontrados");
        result.Should().NotContain("{{");
        result.Should().NotContain("}}");
    }

    [Fact]
    public void Render_NestedInitialPartial_ShowsClassificationAndFinalPromptWithoutTags()
    {
        var renderer = new PromptTemplateRenderer();
        const string template = """
            {{#if is_pending_follow_up}}
            Listo, apliqué estos cambios al pedido:
            {{#each display_applied_items}}
            - {{name}} — cantidad: {{quantity}}
            {{/each}}
            ¿Deseas agregar algo mas?
            {{else}}
            Procesé cada producto de tu solicitud:
            {{#if applied_items}}
            *Agregados*
            {{#each applied_items}}
            {{#if removed}}- Retiré {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} del carrito{{else}}- Agregué o actualicé {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}{{/if}}
            {{/each}}
            {{/if}}
            {{#if unavailable_items}}
            *Sin existencia*
            {{#each unavailable_items}}
            - {{description}}
            {{/each}}
            {{/if}}
            {{#if can_finalize_with_pending}}Si eso es todo, dejaré fuera las referencias sin existencia o sin coincidencia segura. ¿Eso sería todo o deseas agregar algo más?{{else}}Indícame una referencia más precisa.{{/if}}
            {{/if}}
            """;
        var data = new Dictionary<string, object?>
        {
            ["is_pending_follow_up"] = false,
            ["display_applied_items"] = new List<object>(),
            ["applied_items"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["requested_name"] = "maíz",
                    ["name"] = "MAIZ CONGELADO 1 K",
                    ["quantity"] = 2m
                }
            },
            ["unavailable_items"] = new List<object>
            {
                new Dictionary<string, object?> { ["description"] = "chorizo — sin existencia" }
            },
            ["can_finalize_with_pending"] = true
        };

        var result = renderer.Render(template, data);

        result.Should().Contain("Procesé cada producto");
        result.Should().Contain("*Agregados*");
        result.Should().Contain("maíz (MAIZ CONGELADO 1 K) — cantidad: 2");
        result.Should().Contain("*Sin existencia*");
        result.Should().Contain("dejaré fuera las referencias");
        result.Should().NotContain("Listo, aplique este cambio");
        result.Should().NotContain("{{");
        result.Should().NotContain("}}");
    }

    [Fact]
    public void Render_NestedRemovalTemplate_UsesTheCorrectItemAndNeverLeaksTags()
    {
        var renderer = new PromptTemplateRenderer();
        const string template = """
            Listo, apliqué estos cambios al pedido:
            {{#each applied_items}}
            {{#if removed}}- Retiré {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} del carrito{{else}}- Agregué o actualicé {{#if requested_name}}{{requested_name}} ({{name}}){{else}}{{name}}{{/if}} — cantidad: {{quantity}}{{/if}}
            {{/each}}

            ¿Deseas agregar algo mas?
            """;
        var data = new Dictionary<string, object?>
        {
            ["applied_items"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["removed"] = true,
                    ["requested_name"] = "maíz",
                    ["name"] = "MAIZ CONGELADO 1 K",
                    ["quantity"] = null
                },
                new Dictionary<string, object?>
                {
                    ["removed"] = false,
                    ["requested_name"] = "tocinetas",
                    ["name"] = "TOCINETA CJ 1K",
                    ["quantity"] = 4m
                }
            }
        };

        var result = renderer.Render(template, data);

        result.Should().Contain("- Retiré maíz (MAIZ CONGELADO 1 K) del carrito");
        result.Should().Contain("- Agregué o actualicé tocinetas (TOCINETA CJ 1K) — cantidad: 4");
        result.Should().NotContain("Retiré TOCINETA");
        result.Should().NotContain("{{");
        result.Should().NotContain("}}");
    }
}
