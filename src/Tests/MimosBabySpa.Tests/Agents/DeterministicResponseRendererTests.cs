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
    public async Task RenderFollowUp_UsesCurrentStageAndHistoryWithoutPlanningOperations()
    {
        var chat = new RecordingChatClient("\u00bfTe gustar\u00eda continuar con la opci\u00f3n familiar?");
        var renderer = new DeterministicResponseRenderer(chat, new StubPresentationComposer());
        var config = new AgentConfig
        {
            Persona = "Habla con calidez.",
            ConversationFollowUp = new ConversationFollowUpDefinitions
            {
                Enabled = true,
                Guidance = "Recuerda la decisi?n pendiente sin presionar."
            },
            FactSchema =
            [
                new FactSchemaEntry { Key = "plan", Label = "plan", Type = "string", Source = "user" }
            ]
        };
        var stage = new AgentFlowStage
        {
            Id = "choose_plan",
            Goal = "El cliente elige un plan.",
            AdvanceWhenFacts = ["plan"]
        };

        var result = await renderer.RenderFollowUpAsync(new DeterministicFollowUpRequest(
            config,
            stage,
            new Dictionary<string, string>(),
            "Tenemos plan individual y familiar. \u00bfCu\u00e1l prefieres?",
            [ChatMessage.Assistant("Tenemos plan individual y familiar. \u00bfCu\u00e1l prefieres?")]));

        result.Text.Should().Be("\u00bfTe gustar\u00eda continuar con la opci\u00f3n familiar?");
        chat.Prompt.Should().Contain("Recuerda la decisi?n pendiente sin presionar.");
        chat.Prompt.Should().Contain("Tenemos plan individual y familiar");
        chat.Prompt.Should().Contain("Do not invent new facts");
        chat.Options!.MaxTokens.Should().Be(160);
    }

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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Render_UrgentExclusiveTemplate_SuppressesConversationOpening(bool escalated, bool failedOutcome)
    {
        var chat = new RecordingChatClient("should not be used");
        var composer = new StubPresentationComposer("TRANSFERENCIA");
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Saluda y pregunta por el negocio."
            }
        };
        var turn = new DeterministicTurnResult
        {
            Success = true,
            EscalateToHuman = escalated,
            Trace = failedOutcome
                ?
                [
                    new StageOperationTrace(
                        "check", "reservation.check_availability", "{}", "input.past_date", false,
                        Outcome: OperationOutcome.Fail("input.past_date", "past"))
                ]
                : [],
            Response = new StageResponseDefinition { Template = "human_handoff_ack" }
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            new AgentFlowStage { Id = "discovery" },
            turn,
            "Quiero hablar con una persona",
            [ChatMessage.User("Quiero hablar con una persona")],
            RequestOpeningRequired: true));

        response.Text.Should().Be("TRANSFERENCIA");
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
    public async Task Render_WithoutExplicitPresentation_UsesLlmAsTheNormalPath()
    {
        var chat = new RecordingChatClient("respuesta natural");
        var composer = new StubPresentationComposer();
        var renderer = new DeterministicResponseRenderer(chat, composer);

        var response = await renderer.RenderAsync(Request(new DeterministicTurnResult
        {
            Success = true,
            Response = new StageResponseDefinition { Guidance = "Ayuda al cliente a continuar." }
        }));

        response.Text.Should().Be("respuesta natural");
        chat.CallCount.Should().Be(1);
        composer.LastPresentations.Should().BeEmpty();
        chat.Prompt.Should().Contain("responseDirective").And.NotContain("responseGuidance");
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
    public async Task Render_NewRequestWithRememberedIdentity_WritesOpeningAndContinuationInOneCall()
    {
        var expected = $"¡Hola Richard! Bienvenido a CJ Distribuciones, un gusto saludarte 👋{Environment.NewLine}{Environment.NewLine}"
            + "Aquí estoy para ayudarte con tu pedido. ¿Qué deseas el día de hoy? 😊";
        var chat = new RecordingChatClient(expected);
        var composer = new OperationPresentationComposer(new AgentTemplateResolver(), new PromptTemplateRenderer());
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Saluda con cercania"
            },
            FactSchema =
            [
                new FactSchemaEntry { Key = "customer_name", Role = "customer.name", Type = "string" },
                new FactSchemaEntry { Key = "city", Role = "shipping.city", Type = "string" }
            ],
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
            Facts = new Dictionary<string, string>
            {
                ["customer_name"] = "Richard",
                ["city"] = "Valledupar"
            },
            Response = new StageResponseDefinition { Guidance = "Pregunta que desea pedir." }
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            config.Flows[0].Stages[1],
            turn,
            "hola",
            [ChatMessage.User("hola")],
            RequestOpeningRequired: true));

        response.Text.Should().Be(expected);
        chat.CallCount.Should().Be(1);
        chat.Prompt.Should().Contain("openingDirective").And.Contain("\"required\":true");
        chat.Prompt.Should().Contain("Never greet a second time");
        chat.Prompt.Should().Contain("allowedPersonalizationFacts")
            .And.Contain("\"role\":\"customer.name\"");
        chat.Prompt.Should().Contain("\"city\":\"Valledupar\"")
            .And.Contain("do not mention them unless");
    }
    [Fact]
    public async Task Render_NewRequestAskClarification_StillRequiresConversationOpening()
    {
        var expected = "Saludo configurado.\n\nPregunta de aclaracion.";
        var chat = new RecordingChatClient(expected);
        var composer = new OperationPresentationComposer(new AgentTemplateResolver(), new PromptTemplateRenderer());
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Escribe el saludo configurado.",
                AllowQuestions = false
            }
        };
        var turn = new DeterministicTurnResult
        {
            Success = true,
            Response = new StageResponseDefinition
            {
                Mode = "ask_clarification",
                Guidance = "Pregunta por el tipo de negocio."
            }
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            new AgentFlowStage { Id = "discovery" },
            turn,
            "Quiero conocer como pueden ayudarme",
            [ChatMessage.User("Quiero conocer como pueden ayudarme")],
            RequestOpeningRequired: true));

        response.Text.Should().Be(expected);
        chat.CallCount.Should().Be(1);
        chat.Prompt.Should().Contain("\"openingDirective\"")
            .And.Contain("\"required\":true")
            .And.Contain("Escribe el saludo configurado.");
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
    }

    [Fact]
    public async Task Render_NewRequestWithoutName_UsesGenericOpeningAndLetsStageRequestTheName()
    {
        var expected = $"¡Hola! Bienvenido a CJ Distribuciones, es un gusto saludarte 👋{Environment.NewLine}{Environment.NewLine}"
            + "Estoy aquí para ayudarte con tu pedido. Para comenzar, ¿me compartes tu nombre o el de tu negocio? 😊";
        var chat = new RecordingChatClient(expected);
        var composer = new OperationPresentationComposer(new AgentTemplateResolver(), new PromptTemplateRenderer());
        var renderer = new DeterministicResponseRenderer(chat, composer);
        var config = new AgentConfig
        {
            ConversationOpening = new ConversationOpeningDefinitions
            {
                Enabled = true,
                Guidance = "Saluda y da la bienvenida sin inventar un nombre",
                AllowQuestions = false
            }
        };
        var turn = new DeterministicTurnResult
        {
            Success = true
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            new AgentFlowStage
            {
                Id = "customer_name",
                Goal = "Obtener el nombre del cliente.",
                ConversationGuidance = "Solicita el nombre o el nombre del negocio."
            },
            turn,
            "hola",
            [ChatMessage.User("hola")],
            RequestOpeningRequired: true));

        response.Success.Should().BeTrue();
        response.Text.Should().Be(expected).And.NotContain("Richard");
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
            }
        };
        var turn = new DeterministicTurnResult
        {
            Success = true,
            Facts = new Dictionary<string, string> { ["customer_name"] = "Richard" }
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
    public async Task Render_TransientLlmFailure_DoesNotBecomePersistentStageFallback()
    {
        var chat = new TransientFailureChatClient("respuesta recuperada");
        var renderer = new DeterministicResponseRenderer(chat, new StubPresentationComposer());
        var request = Request(new DeterministicTurnResult
        {
            Success = true,
            Response = new StageResponseDefinition { Guidance = "Continua naturalmente." }
        });

        var failed = await renderer.RenderAsync(request);
        var recovered = await renderer.RenderAsync(request);

        failed.Success.Should().BeFalse();
        failed.Text.Should().BeEmpty();
        recovered.Success.Should().BeTrue();
        recovered.Text.Should().Be("respuesta recuperada");
        chat.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Render_ProjectsGenericStageReadinessWithoutTurningBlockersIntoQuestions()
    {
        var chat = new RecordingChatClient("respuesta natural");
        var renderer = new DeterministicResponseRenderer(chat, new StubPresentationComposer());
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "known_user_input", Label = "dato ya conocido", Source = "user" },
                new FactSchemaEntry
                {
                    Key = "required_user_input",
                    Label = "dato requerido del usuario",
                    Source = "user",
                    ExtractionGuidance = "Solicita el dato que falta.",
                    Options =
                    [
                        new FactValueOption { Value = "first", Label = "Primera opcion", Selector = "A" },
                        new FactValueOption { Value = "second", Label = "Segunda opcion", Selector = "B" }
                    ]
                },
                new FactSchemaEntry
                {
                    Key = "optional_user_note",
                    Label = "nota opcional",
                    Source = "user",
                    Required = false
                },
                new FactSchemaEntry
                {
                    Key = "channel_identity",
                    Label = "identidad del canal",
                    Source = "channel"
                },
                new FactSchemaEntry
                {
                    Key = "system_checkpoint",
                    Label = "checkpoint del sistema",
                    Source = "system"
                }
            ]
        };
        var stage = new AgentFlowStage
        {
            Id = "generic_stage",
            AdvanceWhenFacts =
            [
                "known_user_input",
                "required_user_input",
                "channel_identity",
                "system_checkpoint"
            ]
        };

        var response = await renderer.RenderAsync(new DeterministicResponseRequest(
            config,
            stage,
            new DeterministicTurnResult
            {
                Success = true,
                Facts = new Dictionary<string, string> { ["known_user_input"] = "presente" }
            },
            "continuemos",
            [ChatMessage.User("continuemos")]));

        response.Success.Should().BeTrue();
        using var prompt = JsonDocument.Parse(chat.Prompt);
        var readiness = prompt.RootElement.GetProperty("stageReadiness");
        readiness.GetProperty("usesAdvanceFacts").GetBoolean().Should().BeTrue();
        var pending = readiness.GetProperty("pendingBlockers").EnumerateArray().ToList();
        pending.Select(item => item.GetProperty("key").GetString())
            .Should().BeEquivalentTo(["required_user_input", "channel_identity", "system_checkpoint"]);
        pending.Should().NotContain(item =>
            item.GetProperty("key").GetString() == "known_user_input"
            || item.GetProperty("key").GetString() == "optional_user_note",
            "known facts and optional facts outside advanceWhenFacts are not stage blockers");

        var user = pending.Single(item => item.GetProperty("key").GetString() == "required_user_input");
        user.GetProperty("canBeProvidedByCustomer").GetBoolean().Should().BeTrue();
        user.GetProperty("guidance").GetString().Should().Contain("dato que falta");
        var options = user.GetProperty("options").EnumerateArray().ToList();
        options.Should().HaveCount(2);
        options[0].GetProperty("value").GetString().Should().Be("first");
        options[0].GetProperty("label").GetString().Should().Be("Primera opcion");
        options[0].GetProperty("selector").GetString().Should().Be("A");

        var channel = pending.Single(item => item.GetProperty("key").GetString() == "channel_identity");
        channel.GetProperty("canBeProvidedByCustomer").GetBoolean().Should().BeFalse();
        var system = pending.Single(item => item.GetProperty("key").GetString() == "system_checkpoint");
        system.GetProperty("canBeProvidedByCustomer").GetBoolean().Should().BeFalse();

        chat.Prompt.Should().Contain("does not decide what to ask");
        chat.Prompt.Should().Contain("present it as necessary rather than optional");
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
        using var prompt = JsonDocument.Parse(chat.Prompt);
        var readiness = prompt.RootElement.GetProperty("stageReadiness");
        readiness.GetProperty("usesAdvanceFacts").GetBoolean().Should().BeFalse();
        readiness.GetProperty("pendingBlockers").GetArrayLength().Should().Be(0);
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

    private sealed class TransientFailureChatClient(string recoveredResponse) : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                return Task.FromResult(new ChatCompletionResult
                {
                    Success = false,
                    ErrorMessage = "transient renderer failure"
                });
            }

            return Task.FromResult(new ChatCompletionResult
            {
                Success = true,
                Content = recoveredResponse,
                AssistantMessage = ChatMessage.Assistant(recoveredResponse)
            });
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
        private readonly IReadOnlyList<string> _responses;
        public RecordingChatClient(params string[] responses) => _responses = responses;
        public int CallCount { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public ChatCompletionOptions? Options { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var response = _responses[Math.Min(CallCount - 1, _responses.Count - 1)];
            Prompt = messages[0].Content ?? string.Empty;
            Options = options;
            return Task.FromResult(new ChatCompletionResult
            {
                Success = true,
                Content = response,
                AssistantMessage = ChatMessage.Assistant(response)
            });
        }
    }
}
