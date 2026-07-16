using FluentAssertions;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

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
}
