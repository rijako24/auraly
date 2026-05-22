using FluentAssertions;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class PromptTemplateExtractorTests
{
    [Fact]
    public void Extract_FindsTemplateById()
    {
        const string prompt = """
            ## PLANTILLAS

            ### Checkout  [template: checkout_with_deposit]
            ```
            Hola {{customer_name}}
            Total: ${{total}}
            ```
            """;

        var extractor = new PromptTemplateExtractor();
        var template = extractor.Extract(prompt, "checkout_with_deposit");

        template.Should().Contain("{{customer_name}}");
        template.Should().Contain("${{total}}");
    }

    [Fact]
    public void Extract_ReturnsNullWhenMissing()
    {
        var extractor = new PromptTemplateExtractor();
        extractor.Extract("no templates here", "checkout_with_deposit").Should().BeNull();
    }
}
