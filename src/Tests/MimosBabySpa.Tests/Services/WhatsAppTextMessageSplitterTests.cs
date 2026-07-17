using FluentAssertions;
using MimosBabySpa.Infrastructure.Services;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class WhatsAppTextMessageSplitterTests
{
    [Fact]
    public void Split_LongCartResponse_PreservesEveryProductLineAndSection()
    {
        var productLines = Enumerable.Range(1, 18)
            .Select(index => $"- Para producto {index}: REFERENCIA COMERCIAL {index} — $12,345.67 COP")
            .ToArray();
        var message = string.Join("\n\n",
            "Procesé cada producto de tu solicitud:",
            $"*Necesito que elijas*\n{string.Join('\n', productLines)}",
            "*Pedido actual*\n- PRODUCTO CONFIRMADO — cantidad: 2\n\n*Total: $24,691.34 COP*");

        var chunks = WhatsAppTextMessageSplitter.Split(message, 500);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Length <= 500);
        foreach (var line in productLines)
            chunks.Should().Contain(chunk => chunk.Contains(line, StringComparison.Ordinal));
        chunks.Should().Contain(chunk => chunk.Contains("*Pedido actual*", StringComparison.Ordinal));
    }

    [Fact]
    public void Split_ShortResponse_ReturnsSingleUnchangedMessage()
    {
        const string message = "Listo, agregué el producto.";
        WhatsAppTextMessageSplitter.Split(message, 1800).Should().Equal(message);
    }
}
