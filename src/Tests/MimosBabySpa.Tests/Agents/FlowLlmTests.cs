using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Orchestration;
using MimosBabySpa.Application.LLM;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FlowLlmTests
{
    [Fact]
    public async Task RunAsync_with_tools_forces_json_response_and_returns_structured_content()
    {
        var captured = new CapturingChatClient();
        var flowLlm = new FlowLlm(captured, NullLogger<FlowLlm>.Instance);

        var request = new FlowLlmRequest
        {
            Config = new AgentConfig
            {
                Temperature = 0.5f,
                OperationalLimits = new AgentOperationalLimits { MaxResponseTokens = 100 },
                Model = "test-model"
            },
            AvailableTools = [ new ChatToolDefinition { Name = "dummy", Description = "dummy tool", ParametersJson = "{}" } ],
            History = [],
            KnownFacts = new Dictionary<string, string>(),
            UserMessage = "Hola",
            Stage = new AgentFlowStage { Id = "discovery", Ask = "ask" },
            RenderedTemplate = null
        };

        captured.NextResult = new ChatCompletionResult
        {
            Success = true,
            FinishReason = ChatCompletionFinishReason.Stop,
            Content = "{\"intent\":\"Continue\",\"facts\":{},\"reply\":\"Ok\"}",
            AssistantMessage = ChatMessage.Assistant("{\"intent\":\"Continue\",\"facts\":{},\"reply\":\"Ok\"}"),
            PromptTokens = 5,
            CompletionTokens = 10
        };

        var result = await flowLlm.RunAsync(request, CancellationToken.None);

        result.Intent.Should().Be("Continue");
        result.Reply.Should().Be("Ok");
        captured.Options.ForceJsonResponse.Should().BeTrue();
        captured.Options.ForceTextResponse.Should().BeFalse();
        captured.Tools.Should().ContainSingle().Which.Name.Should().Be("dummy");
    }

    [Fact]
    public async Task RunAsync_system_prompt_includes_set_fact_stage_collects_when_tool_available()
    {
        var captured = new CapturingChatClient();
        var flowLlm = new FlowLlm(captured, NullLogger<FlowLlm>.Instance);

        var request = new FlowLlmRequest
        {
            Config = new AgentConfig
            {
                Temperature = 0.5f,
                OperationalLimits = new AgentOperationalLimits { MaxResponseTokens = 100 },
                Model = "test-model",
                FactSchema =
                [
                    new FactSchemaEntry { Key = "baby_name", Label = "Nombre del bebé", Type = "string" },
                    new FactSchemaEntry { Key = "baby_age_months", Label = "Edad en meses", Type = "number" }
                ]
            },
            AvailableTools =
            [
                new ChatToolDefinition
                {
                    Name = "set_fact",
                    Description = "Persist a fact",
                    ParametersJson = "{}"
                }
            ],
            StageCollects = ["baby_name", "baby_age_months"],
            History = [],
            KnownFacts = new Dictionary<string, string>(),
            UserMessage = "quiero llevar a mi bebe al spa",
            Stage = new AgentFlowStage
            {
                Id = "discovery",
                Ask = "Ask for the baby name and age."
            }
        };

        captured.NextResult = new ChatCompletionResult
        {
            Success = true,
            FinishReason = ChatCompletionFinishReason.Stop,
            Content = "{\"intent\":\"Continue\",\"reply\":\"¿Cómo se llama tu bebé?\"}",
            AssistantMessage = ChatMessage.Assistant("{\"intent\":\"Continue\",\"reply\":\"¿Cómo se llama tu bebé?\"}")
        };

        await flowLlm.RunAsync(request, CancellationToken.None);

        var systemPrompt = captured.Messages.Should().ContainSingle(m => m.Role == ChatRole.System).Subject.Content;
        systemPrompt.Should().Contain("DATOS DE ESTE STAGE");
        systemPrompt.Should().Contain("baby_name");
        systemPrompt.Should().Contain("baby_age_months");
        systemPrompt.Should().Contain("set_fact");
        systemPrompt.Should().NotContain("CAPTURA ANTICIPADA");
    }

    [Fact]
    public async Task RunAsync_llm_curate_prompt_lists_all_plans_exhaustively()
    {
        var captured = new CapturingChatClient();
        var flowLlm = new FlowLlm(captured, NullLogger<FlowLlm>.Instance);

        var request = new FlowLlmRequest
        {
            Config = new AgentConfig
            {
                Temperature = 0.3f,
                OperationalLimits = new AgentOperationalLimits { MaxResponseTokens = 100 },
                Model = "test-model"
            },
            AvailableTools = [],
            StageCollects = ["service"],
            History = [],
            KnownFacts = new Dictionary<string, string> { ["baby_age_months"] = "5" },
            UserMessage = "muéstrame planes",
            Stage = new AgentFlowStage
            {
                Id = "service_presentation",
                Ask = "Present plans",
                LookupPresentation = FlowStageLookupPresentation.LlmCurate
            },
            LookupResult = FlowToolResult.Parse(
                """{"ok":true,"data":{"services":[{"name":"Plan A","price":100}]}}""")
        };

        captured.NextResult = new ChatCompletionResult
        {
            Success = true,
            FinishReason = ChatCompletionFinishReason.Stop,
            Content = "{\"intent\":\"Continue\",\"reply\":\"Planes\"}",
            AssistantMessage = ChatMessage.Assistant("{\"intent\":\"Continue\",\"reply\":\"Planes\"}")
        };

        await flowLlm.RunAsync(request, CancellationToken.None);

        var systemPrompt = captured.Messages.Should().ContainSingle(m => m.Role == ChatRole.System).Subject.Content;
        systemPrompt.Should().Contain("Lista TODOS los planes");
        systemPrompt.Should().Contain("Ante duda: incluir");
        systemPrompt.Should().NotContain("Muestra **solo**");
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public ChatCompletionOptions? Options { get; private set; }
        public IReadOnlyList<ChatToolDefinition>? Tools { get; private set; }
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
        public ChatCompletionResult? NextResult { get; set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ChatToolDefinition>? tools = null,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            Tools = tools;
            Messages = messages;
            return Task.FromResult(NextResult ?? new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = "{\"intent\":\"Continue\",\"facts\":{},\"reply\":\"Ok\"}",
                AssistantMessage = ChatMessage.Assistant("{\"intent\":\"Continue\",\"facts\":{},\"reply\":\"Ok\"}"),
            });
        }
    }
}
