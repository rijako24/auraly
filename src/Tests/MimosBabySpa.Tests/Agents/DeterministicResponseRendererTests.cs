using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.LLM;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DeterministicResponseRendererTests
{
    [Fact]
    public async Task Render_ExclusivePresentation_SkipsLlmAndReturnsTemplateOnly()
    {
        var chat = new RecordingChatClient("should not be used");
        var composer = new StubPresentationComposer("HORARIOS");
        var renderer = new DeterministicResponseRenderer(chat, composer);

        var response = await renderer.RenderAsync(Request(new DeterministicTurnResult
        {
            Success = true,
            Presentations =
            [
                new OperationPresentation(
                    "availability_slots",
                    new Dictionary<string, object?>(),
                    FragmentRenderMode.Exclusive,
                    FragmentPriority.Required)
            ]
        }));

        response.Text.Should().Be("HORARIOS");
        chat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Render_ConfiguredResponseTemplate_IsExclusiveAndSkipsLlm()
    {
        var chat = new RecordingChatClient("should not be used");
        var composer = new StubPresentationComposer("UN SOLO PEDIDO");
        var renderer = new DeterministicResponseRenderer(chat, composer);

        var response = await renderer.RenderAsync(Request(new DeterministicTurnResult
        {
            Success = true,
            Facts = new Dictionary<string, string> { ["customer_name"] = "Ana" },
            Response = new StageResponseDefinition { Template = "single_active_order_required" }
        }));

        response.Text.Should().Be("UN SOLO PEDIDO");
        chat.CallCount.Should().Be(0);
        composer.LastPresentations.Should().ContainSingle();
        composer.LastPresentations[0].TemplateId.Should().Be("single_active_order_required");
        composer.LastPresentations[0].Mode.Should().Be(FragmentRenderMode.Exclusive);
    }
    [Fact]
    public async Task Render_FallbackTemplateWithoutAuthoritativePresentation_SkipsLlm()
    {
        var chat = new RecordingChatClient("invented catalog response");
        var composer = new StubPresentationComposer("SAFE FALLBACK");
        var renderer = new DeterministicResponseRenderer(chat, composer);

        var response = await renderer.RenderAsync(Request(new DeterministicTurnResult
        {
            Success = true,
            Response = new StageResponseDefinition { FallbackTemplate = "catalog_prompt" }
        }));

        response.Text.Should().Be("SAFE FALLBACK");
        chat.CallCount.Should().Be(0);
        composer.LastPresentations.Should().ContainSingle();
        composer.LastPresentations[0].TemplateId.Should().Be("catalog_prompt");
        composer.LastPresentations[0].Mode.Should().Be(FragmentRenderMode.Exclusive);
    }
    [Fact]
    public async Task Render_SuppressedText_LeavesConfiguredSequenceAsTheOnlyChannelOutput()
    {
        var chat = new RecordingChatClient("duplicate confirmation");
        var composer = new StubPresentationComposer();
        var renderer = new DeterministicResponseRenderer(chat, composer);

        var response = await renderer.RenderAsync(Request(new DeterministicTurnResult
        {
            Success = true,
            Response = new StageResponseDefinition { SuppressText = true },
            Sequences = ["order_created_customer"]
        }));

        response.Text.Should().BeEmpty();
        chat.CallCount.Should().Be(0);
    }
    [Fact]
    public async Task Render_NormalOutcome_UsesLlmOnlyAsLanguageRenderer()
    {
        var chat = new RecordingChatClient("Respuesta natural");
        var composer = new StubPresentationComposer();
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var outcome = OperationOutcome.Ok("catalog.services_returned", new { catalog = "Corte infantil: 30000" });

        var response = await renderer.RenderAsync(Request(new DeterministicTurnResult
        {
            Success = true,
            Trace =
            [
                new StageOperationTrace(
                    "catalog",
                    "catalog.get_services",
                    "{}",
                    outcome.Code,
                    true,
                    Outcome: outcome)
            ]
        }));

        response.Text.Should().Be("Respuesta natural");
        chat.CallCount.Should().Be(1);
        chat.Prompt.Should().Contain("Corte infantil: 30000");
        chat.Prompt.Should().Contain("configured persona and policies");
    }

    private static DeterministicResponseRequest Request(DeterministicTurnResult turn) => new(
        new AgentConfig { Persona = "Habla con calidez" },
        new AgentFlowStage { Id = "stage", ConversationGuidance = "Pregunta solo lo necesario" },
        turn,
        "hola",
        []);

    private sealed class StubPresentationComposer : IOperationPresentationComposer
    {
        private readonly string? _fixed;
        public StubPresentationComposer(string? fixedValue = null) => _fixed = fixedValue;

        public IReadOnlyList<OperationPresentation> LastPresentations { get; private set; } = [];

        public string Compose(AgentConfig config, string? llmResponse, IReadOnlyList<OperationPresentation> presentations)
        {
            LastPresentations = presentations;
            return _fixed ?? llmResponse ?? string.Empty;
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly string _response;
        public RecordingChatClient(string response) => _response = response;
        public int CallCount { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public ChatCompletionOptions? Options { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Prompt = messages[0].Content ?? string.Empty;
            Options = options;
            return Task.FromResult(new ChatCompletionResult
            {
                Success = true,
                Content = _response,
                AssistantMessage = ChatMessage.Assistant(_response)
            });
        }
    }
}
