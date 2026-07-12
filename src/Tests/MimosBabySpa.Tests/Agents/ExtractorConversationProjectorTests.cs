using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.LLM;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ExtractorConversationProjectorTests
{
    [Fact]
    public void DefaultWindowKeepsOnlyTheImmediateUserAssistantExchange()
    {
        var config = new AgentConfig { HistoryWindowSize = 20, ExtractorHistoryWindowSize = 2 };
        var history = new[]
        {
            ChatMessage.User("consulta antigua"),
            ChatMessage.Assistant("respuesta antigua"),
            ChatMessage.User("que tienes de cerdo"),
            ChatMessage.Assistant("estas son las opciones de cerdo")
        };

        var projected = ExtractorConversationProjector.Project(config, history);

        projected.Select(message => message.Content).Should().Equal(
            "que tienes de cerdo",
            "estas son las opciones de cerdo");
    }

    [Fact]
    public void WindowIsConfigurableButCannotExceedTheLoadedConversationWindow()
    {
        var config = new AgentConfig { HistoryWindowSize = 3, ExtractorHistoryWindowSize = 10 };
        var history = Enumerable.Range(1, 5).Select(index => ChatMessage.User($"m{index}")).ToList();

        var projected = ExtractorConversationProjector.Project(config, history);

        projected.Select(message => message.Content).Should().Equal("m3", "m4", "m5");
    }
}
