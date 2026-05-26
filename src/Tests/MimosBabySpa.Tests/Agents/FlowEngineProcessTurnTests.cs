using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Orchestration;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Models;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

/// <summary>
/// Tests del bucle único de <see cref="FlowEngine.ProcessTurnAsync"/>.
/// </summary>
public sealed class FlowEngineProcessTurnTests
{
    [Fact]
    public async Task ProcessTurnAsync_verbatim_stage_does_not_call_llm()
    {
        var llm = new QueueingFlowLlm();
        var engine = CreateEngine(llm);
        var config = BuildConfig(
        [
            new AgentFlowStage
            {
                Id = "greeting",
                Verbatim = "¡Hola!",
                CompletedWhen = StageCompletionCriteria.Always
            }
        ]);
        var session = AgentTestHelpers.CreateSession(config);
        session.ConversationState.CompletedOneShotStages.Clear();

        var result = await engine.ProcessTurnAsync(config, session, "hola", CancellationToken.None);

        result.Response.Should().Be("¡Hola!");
        llm.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessTurnAsync_same_turn_stage_advance_returns_only_final_reply()
    {
        var llm = new QueueingFlowLlm();
        llm.Enqueue(new FlowTurnResult
        {
            ToolCalls =
            [
                new ToolCallRequest
                {
                    Id = "c1",
                    FunctionName = "set_fact",
                    ArgumentsJson = """{"key":"baby_name","value":"Thomas"}"""
                },
                new ToolCallRequest
                {
                    Id = "c2",
                    FunctionName = "set_fact",
                    ArgumentsJson = """{"key":"baby_age_months","value":"5"}"""
                }
            ]
        });
        llm.Enqueue(new FlowTurnResult
        {
            Reply = "Planes para Thomas de 5 meses.",
            Intent = "Continue"
        });

        var engine = CreateEngine(llm);
        var config = BuildConfig(
        [
            new AgentFlowStage
            {
                Id = "greeting",
                Verbatim = "Hola",
                CompletedWhen = StageCompletionCriteria.Always
            },
            new AgentFlowStage
            {
                Id = "discovery",
                Ask = "Capture baby facts",
                AllowedTools = ["set_fact"],
                Collects = ["baby_name", "baby_age_months"],
                CompletedWhen = StageCompletionCriteria.FactsCollected
            },
            new AgentFlowStage
            {
                Id = "service_presentation",
                Ask = "Present plans",
                Lookup = new AgentFlowStageLookup { Tool = "get_service_catalog", Args = new Dictionary<string, string>() },
                LookupPresentation = FlowStageLookupPresentation.LlmCurate,
                AllowedTools = ["set_fact"],
                Collects = ["service"],
                CompletedWhen = StageCompletionCriteria.FactsCollected
            }
        ]);

        var session = AgentTestHelpers.CreateSession(config);
        session.ConversationState.CompletedOneShotStages.Add("greeting");

        var result = await engine.ProcessTurnAsync(
            config, session, "se llama thomas y tiene 5 meses", CancellationToken.None);

        result.Response.Should().Be("Planes para Thomas de 5 meses.");
        result.Response.Should().NotContain("Perfecto");
        llm.CallCount.Should().Be(2);
        llm.StageIds.Should().Equal("discovery", "service_presentation");
        session.Facts["baby_name"].Should().Be("Thomas");
        session.Facts["baby_age_months"].Should().Be("5");
        session.ConversationState.CompletedOneShotStages.Should().Contain("discovery");
    }

    [Fact]
    public async Task ProcessTurnAsync_lookup_reruns_after_set_fact_in_same_turn()
    {
        var llm = new QueueingFlowLlm();
        llm.Enqueue(new FlowTurnResult
        {
            ToolCalls =
            [
                new ToolCallRequest
                {
                    Id = "c1",
                    FunctionName = "set_fact",
                    ArgumentsJson = """{"key":"desired_date","value":"2026-05-27"}"""
                }
            ]
        });
        llm.Enqueue(new FlowTurnResult { Reply = "Horarios disponibles", Intent = "Continue" });

        var availabilityCalls = 0;
        var engine = CreateEngine(llm, onAvailabilityCall: () => availabilityCalls++);
        var config = BuildConfig(
        [
            new AgentFlowStage
            {
                Id = "scheduling",
                Ask = "Schedule",
                Lookup = new AgentFlowStageLookup
                {
                    Tool = "check_availability",
                    Args = new Dictionary<string, string>
                    {
                        ["service"] = "@fact.service",
                        ["date"] = "@fact.desired_date",
                        ["time"] = "@fact.desired_time"
                    }
                },
                Template = "@result.template_id",
                AllowedTools = ["set_fact", "check_availability"],
                Collects = ["desired_date", "desired_time"],
                CompletedWhen = StageCompletionCriteria.FactsCollected
            }
        ]);

        var session = AgentTestHelpers.CreateSession(config);
        session.Facts["service"] = "Plan Marineritos";

        await engine.ProcessTurnAsync(config, session, "que espacios tienes para mañana", CancellationToken.None);

        availabilityCalls.Should().Be(2);
        session.Facts["desired_date"].Should().Be("2026-05-27");
    }

    [Fact]
    public async Task ProcessTurnAsync_continues_to_customer_data_when_scheduling_completes_with_empty_reply()
    {
        var llm = new QueueingFlowLlm();
        llm.Enqueue(new FlowTurnResult
        {
            ToolCalls =
            [
                new ToolCallRequest
                {
                    Id = "c1",
                    FunctionName = "set_fact",
                    ArgumentsJson = """{"key":"desired_time","value":"08:00"}"""
                },
                new ToolCallRequest
                {
                    Id = "c2",
                    FunctionName = "check_availability",
                    ArgumentsJson = """{"service":"Plan Marineritos","date":"2026-05-27","time":"08:00"}"""
                }
            ]
        });
        llm.Enqueue(new FlowTurnResult { Reply = "¿Cuál es tu nombre completo?", Intent = "Continue" });

        var engine = CreateEngine(llm);
        var config = BuildConfig(
        [
            new AgentFlowStage
            {
                Id = "scheduling",
                Ask = "Agendar",
                Lookup = new AgentFlowStageLookup
                {
                    Tool = "check_availability",
                    Args = new Dictionary<string, string>
                    {
                        ["service"] = "@fact.service",
                        ["date"] = "@fact.desired_date",
                        ["time"] = "@fact.desired_time"
                    }
                },
                AllowedTools = ["set_fact", "check_availability"],
                Collects = ["desired_date", "desired_time", "result:slot_confirmed=true"],
                CompletedWhen = StageCompletionCriteria.FactsCollected
            },
            new AgentFlowStage
            {
                Id = "customer_data",
                Ask = "Nombre del cliente",
                AllowedTools = ["set_fact"],
                Collects = ["customer_name"],
                CompletedWhen = StageCompletionCriteria.FactsCollected
            }
        ]);

        var session = AgentTestHelpers.CreateSession(config);
        session.Facts["service"] = "Plan Marineritos";
        session.Facts["desired_date"] = "2026-05-27";
        session.ConversationState.CompletedOneShotStages.Add("greeting");
        session.ConversationState.CompletedOneShotStages.Add("discovery");
        session.ConversationState.CompletedOneShotStages.Add("service_presentation");
        session.ConversationState.CompletedOneShotStages.Add("addons_offering");

        var result = await engine.ProcessTurnAsync(config, session, "si", CancellationToken.None);

        llm.StageIds.Should().Contain("scheduling");
        llm.StageIds.Should().Contain("customer_data");
        result.Response.Should().Contain("nombre completo");
        session.ConversationState.CompletedOneShotStages.Should().Contain("scheduling");
    }

    private static FlowEngine CreateEngine(
        QueueingFlowLlm llm,
        Action? onCatalogCall = null,
        Action? onAvailabilityCall = null)
    {
        var messageService = new Mock<IMessageService>();
        messageService
            .Setup(m => m.GetConversationHistoryAsync(It.IsAny<Guid>()))
            .ReturnsAsync([]);

        var stateManager = new Mock<IConversationStateManager>();
        stateManager
            .Setup(m => m.SaveStateAsync(It.IsAny<Guid>(), It.IsAny<ConversationState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ConversationState state, CancellationToken _) => state);

        var lifecycle = new Mock<IConversationLifecycleService>();
        lifecycle
            .Setup(l => l.TouchActivityAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IAgentTool[] tools =
        [
            new StubSetFactTool(),
            new StubCatalogTool(onCatalogCall),
            new StubAvailabilityTool(onAvailabilityCall)
        ];

        var registry = new AgentToolRegistry(tools, NullLogger<AgentToolRegistry>.Instance);

        return new FlowEngine(
            llm,
            AgentTestHelpers.CreateToolCapabilityGate(),
            AgentTestHelpers.CreateRoleFactResolver(),
            new MockTemplateRenderer(),
            messageService.Object,
            stateManager.Object,
            lifecycle.Object,
            registry,
            NullLogger<FlowEngine>.Instance);
    }

    private static AgentConfig BuildConfig(IReadOnlyList<AgentFlowStage> stages) =>
        new()
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Model = "test",
            Temperature = 0.7f,
            MaxToolIterations = 6,
            EnabledToolNames = ["set_fact", "get_service_catalog", "check_availability"],
            CapabilityPacks = ["booking"],
            FactSchema = AgentTestHelpers.MimiFactSchema,
            HumanMessages = AgentTestHelpers.DefaultHumanMessages(),
            PromptSections = AgentTestHelpers.DefaultPromptSections(),
            Flow = new AgentFlowDefinition { Stages = stages },
            OperationalLimits = new AgentOperationalLimits()
        };

    private sealed class QueueingFlowLlm : IFlowLlm
    {
        private readonly Queue<FlowTurnResult> _queue = new();

        public int CallCount { get; private set; }
        public List<string> StageIds { get; } = [];

        public void Enqueue(FlowTurnResult result) => _queue.Enqueue(result);

        public Task<FlowTurnResult> RunAsync(FlowLlmRequest request, CancellationToken ct)
        {
            CallCount++;
            StageIds.Add(request.Stage?.Id ?? "<none>");

            if (_queue.Count == 0)
                throw new InvalidOperationException("No queued LLM results.");

            return Task.FromResult(_queue.Dequeue());
        }
    }

    private sealed class StubSetFactTool : IAgentTool
    {
        public string Name => "set_fact";
        public string Description => "set_fact";
        public string ParametersSchema => "{}";

        public Task<string> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            if (!ToolResultHelper.TryGetString(invocation.Arguments, "key", out var key)
                || !ToolResultHelper.TryGetString(invocation.Arguments, "value", out var value))
            {
                return Task.FromResult(ToolResultHelper.MissingPrerequisites(["key", "value"]));
            }

            invocation.Context.Facts[key] = value;
            return Task.FromResult(ToolResultHelper.Ok(new { key, value, storage = "fact" }));
        }
    }

    private sealed class StubAvailabilityTool(Action? onCall) : IAgentTool
    {
        public string Name => "check_availability";
        public string Description => "availability";
        public string ParametersSchema => "{}";

        public Task<string> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            onCall?.Invoke();
            var hasDate = ToolResultHelper.TryGetString(invocation.Arguments, "date", out var date)
                && !string.IsNullOrWhiteSpace(date);

            if (!hasDate)
                return Task.FromResult(ToolResultHelper.Error("invalid_args", "Parameter 'date' is required."));

            var hasTime = ToolResultHelper.TryGetString(invocation.Arguments, "time", out var time)
                && !string.IsNullOrWhiteSpace(time);

            if (hasTime)
            {
                return Task.FromResult("""
                    {"ok":true,"data":{"slot_confirmed":true,"time":"08:00","available_slots":[],"verbal_status":"horario_disponible_no_reservado"}}
                    """);
            }

            return Task.FromResult("""
                {"ok":true,"data":{"slot_confirmed":false,"template_id":"availability_slots","available_slots":["08:00","09:00"],"template_data":{"service_name":"Plan","date_formatted":"27/05/2026","slots":["08:00","09:00"]}}}
                """);
        }
    }

    private sealed class StubCatalogTool(Action? onCall) : IAgentTool
    {
        public string Name => "get_service_catalog";
        public string Description => "catalog";
        public string ParametersSchema => "{}";

        public Task<string> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            onCall?.Invoke();
            return Task.FromResult("""
                {"ok":true,"data":{"services":[{"name":"Plan A","price":100}],"template_id":"service_catalog_summary","template_data":{"services":[],"currency":"COP"}}}
                """);
        }
    }

    private sealed class MockTemplateRenderer : MimosBabySpa.Application.Agents.Templates.ITemplateRenderer
    {
        public string Render(string template, IReadOnlyDictionary<string, object?> data) => template;
    }
}
