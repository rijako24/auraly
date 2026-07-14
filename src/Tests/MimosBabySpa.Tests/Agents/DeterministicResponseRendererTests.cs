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

    [Fact]
    public async Task Render_NewRequestWithRememberedIdentity_PrependsConfiguredGreetingOnce()
    {
        var chat = new RecordingChatClient("Hola, Richard! Que gusto saludarte.");
        var composer = new OperationPresentationComposer(new AgentTemplateResolver(), new PromptTemplateRenderer());
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Saluda con cercania"
            },
            Templates = new Dictionary<string, string>
            {
                ["product_prompt"] = "Que deseas pedir?"
            },
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "order",
                    Type = FlowTypes.Primary,
                    Stages =
                    [
                        new AgentFlowStage { Id = "identity" },
                        new AgentFlowStage { Id = "products" }
                    ]
                }
            ]
        };
        var turn = new DeterministicTurnResult
        {
            Success = true,
            Facts = new Dictionary<string, string> { ["customer_name"] = "Richard" },
            Response = new StageResponseDefinition { FallbackTemplate = "product_prompt" }
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            config.Flows[0].Stages[1],
            turn,
            "hola",
            [ChatMessage.User("hola")],
            RequestOpeningRequired: true));

        response.Text.Should().Be($"Hola, Richard! Que gusto saludarte.{Environment.NewLine}{Environment.NewLine}Que deseas pedir?");
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Render_FirstGreetingWithCatalogPresentation_PrependsOpeningAndSuppressesIdentityFallback()
    {
        var chat = new RecordingChatClient("Hola! Bienvenido a CJ Distribuciones.");
        var composer = new StubPresentationComposer("CATALOGO OFICIAL");
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Saluda y da la bienvenida a CJ Distribuciones",
                AllowQuestions = false
            }
        };
        var turn = new DeterministicTurnResult
        {
            Success = true,
            Response = new StageResponseDefinition { FallbackTemplate = "customer_name_prompt" },
            Presentations =
            [
                new OperationPresentation(
                    "catalog_results",
                    new Dictionary<string, object?>(),
                    FragmentRenderMode.Exclusive,
                    FragmentPriority.Required)
            ]
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            new AgentFlowStage { Id = "customer_name" },
            turn,
            "hola, que pechugas tienes?",
            [ChatMessage.User("hola, que pechugas tienes?")],
            RequestOpeningRequired: true));

        response.Text.Should().Be(
            $"Hola! Bienvenido a CJ Distribuciones.{Environment.NewLine}{Environment.NewLine}CATALOGO OFICIAL");
        chat.CallCount.Should().Be(1);
        composer.LastPresentations.Should().ContainSingle();
        composer.LastPresentations[0].TemplateId.Should().Be("catalog_results");
        composer.LastPresentations.Should().NotContain(presentation =>
            presentation.TemplateId == "customer_name_prompt");
    }

    [Fact]
    public async Task Render_OpeningQuestionIsRejected_AndCannotDuplicateTheStagePrompt()
    {
        var chat = new RecordingChatClient("¡Hola Richard! Aquí estoy para ayudarte. ¿Qué te gustaría incluir hoy?");
        var composer = new OperationPresentationComposer(new AgentTemplateResolver(), new PromptTemplateRenderer());
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Saluda y da la bienvenida, sin preguntas",
                AllowQuestions = false
            },
            Templates = new Dictionary<string, string>
            {
                ["product_prompt"] = "Cuentame que productos necesitas."
            }
        };
        var turn = new DeterministicTurnResult
        {
            Success = true,
            Facts = new Dictionary<string, string> { ["customer_name"] = "Richard" },
            Response = new StageResponseDefinition { FallbackTemplate = "product_prompt" }
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            new AgentFlowStage { Id = "products" },
            turn,
            "hola",
            [ChatMessage.User("hola")],
            RequestOpeningRequired: true));

        response.Success.Should().BeFalse();
        response.Text.Should().BeEmpty();
        response.ErrorMessage.Should().Contain("presentation policy");
        chat.CallCount.Should().Be(1);
    }
    [Fact]
    public async Task Render_OpeningLlmFailure_IsReportedToGeneralFailureHandler()
    {
        var chat = new FailingChatClient();
        var composer = new OperationPresentationComposer(new AgentTemplateResolver(), new PromptTemplateRenderer());
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Saluda con cercania"
            },
            Templates = new Dictionary<string, string>
            {
                ["product_prompt"] = "Que deseas pedir?"
            }
        };
        var turn = new DeterministicTurnResult
        {
            Success = true,
            Facts = new Dictionary<string, string> { ["customer_name"] = "Richard" },
            Response = new StageResponseDefinition { FallbackTemplate = "product_prompt" }
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            new AgentFlowStage { Id = "products" },
            turn,
            "hola",
            [ChatMessage.User("hola")],
            RequestOpeningRequired: true));

        response.Success.Should().BeFalse();
        response.Text.Should().BeEmpty();
        response.ErrorMessage.Should().Contain("simulated renderer failure");
        chat.CallCount.Should().Be(1);
    }
    [Fact]
    public async Task Render_OngoingRequest_DoesNotRepeatConversationGreeting()
    {
        var chat = new RecordingChatClient("respuesta normal");
        var composer = new OperationPresentationComposer(new AgentTemplateResolver(), new PromptTemplateRenderer());
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions { Enabled = true, Guidance = "Saluda" },
            Templates = new Dictionary<string, string> { ["request_opening"] = "Hola de nuevo" },
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "order",
                    Type = FlowTypes.Primary,
                    Stages = [new AgentFlowStage { Id = "identity" }, new AgentFlowStage { Id = "products" }]
                }
            ]
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            config.Flows[0].Stages[1],
            new DeterministicTurnResult { Success = true },
            "quiero pollo",
            [ChatMessage.Assistant("Hola"), ChatMessage.User("quiero pollo")]));

        response.Text.Should().Be("respuesta normal");
        chat.CallCount.Should().Be(1);
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

    private sealed class FailingChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatCompletionResult
            {
                Success = false,
                ErrorMessage = "simulated renderer failure"
            });
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
