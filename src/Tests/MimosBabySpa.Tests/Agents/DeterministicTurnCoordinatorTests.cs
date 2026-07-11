using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DeterministicTurnCoordinatorTests
{
    [Fact]
    public async Task Execute_MapsStructuredSignalToConfiguredOperation_AndAdvancesDeterministically()
    {
        var operation = new CapturingOperation();
        var factStore = new RecordingFactStore();
        var coordinator = new DeterministicTurnCoordinator(
            new StubPlanner(Plan()),
            new DeterministicFlowSelector(),
            new FactMutationBatchProcessor(),
            factStore,
            new ConversationVerificationService(),
            new DeterministicStageExecutor(
                new AgentOperationRegistry([operation]),
                new StageConditionEvaluator(),
                new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));
        var config = Config();

        var result = await coordinator.ExecuteAsync(new DeterministicTurnRequest
        {
            Config = config,
            OperationContext = new OperationContext
            {
                AgentId = Guid.NewGuid(),
                BusinessId = Guid.NewGuid(),
                ConversationId = Guid.NewGuid(),
                BusinessToday = new DateOnly(2026, 7, 10),
                BusinessNow = DateTimeOffset.UtcNow,
                Config = config,
                ConversationState = new ConversationState()
            },
            CurrentFlowId = "primary",
            CurrentStageId = "capture",
            LatestUserMessage = "dos elementos"
        });

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.VisitedStages.Should().Equal("capture", "done");
        result.Facts["processed"].Should().Be("true");
        operation.Input.GetProperty("payload").GetArrayLength().Should().Be(2);
        factStore.Batches.Should().ContainSingle(batch => batch["processed"] == "true");
    }

    [Fact]
    public async Task Execute_DefersWholePlanUntilAmbiguousFactIsResolved()
    {
        var operation = new CapturingOperation();
        var factStore = new RecordingFactStore();
        var planner = new QueuePlanner(
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
                Signals = [new PlannedSignal { Type = "domain_changes", Value = Json("[\"uno\",\"dos\"]"), Evidence = "uno y dos" }],
                Response = new TurnPlanResponseDirective { Mode = "ask_clarification", AmbiguousFields = ["address"] }
            },
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
                Facts =
                [
                    new PlannedFactClaim
                    {
                        Key = "address", Operation = TurnPlanOperations.Set,
                        Value = Json("\"Calle 10\""), Evidence = "Calle 10"
                    }
                ]
            });
        var coordinator = new DeterministicTurnCoordinator(
            planner, new DeterministicFlowSelector(), new FactMutationBatchProcessor(), factStore,
            new ConversationVerificationService(),
            new DeterministicStageExecutor(new AgentOperationRegistry([operation]), new StageConditionEvaluator(), new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));
        var config = Config();
        var state = new ConversationState();
        var context = new OperationContext
        {
            AgentId = Guid.NewGuid(), BusinessId = Guid.NewGuid(), ConversationId = Guid.NewGuid(),
            BusinessToday = new DateOnly(2026, 7, 10), BusinessNow = DateTimeOffset.UtcNow,
            Config = config, ConversationState = state
        };

        var first = await coordinator.ExecuteAsync(Request(config, context, new Dictionary<string, string>(), new Dictionary<string, long>()));
        first.Trace.Should().BeEmpty();
        state.PendingTurnPlan.Should().NotBeNull();

        var second = await coordinator.ExecuteAsync(Request(config, context, first.Facts, first.FactVersions));
        second.Success.Should().BeTrue(string.Join("; ", second.Errors));
        second.Facts["address"].Should().Be("Calle 10");
        operation.Input.GetProperty("payload").GetArrayLength().Should().Be(2);
        state.PendingTurnPlan.Should().BeNull();
    }

    private static DeterministicTurnRequest Request(
        AgentConfig config,
        OperationContext context,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyDictionary<string, long> versions) => new()
    {
        Config = config, OperationContext = context, CurrentFacts = facts, FactVersions = versions,
        CurrentFlowId = "primary", CurrentStageId = "capture", ActiveFlowId = "primary",
        LatestUserMessage = "mensaje"
    };

    private static AgentConfig Config()
    {
        var capture = new AgentFlowStage
        {
            Id = "capture",
            Collect = ["address"],
            Signals =
            [
                new StageSignalDefinition
                {
                    Type = "domain_changes",
                    ValueSchema = Json("{\"type\":\"array\",\"items\":{\"type\":\"string\"}}")
                }
            ],
            Actions =
            [
                new StageActionDefinition
                {
                    Id = "apply_changes",
                    Operation = CapturingOperation.Id,
                    Trigger = StageActionTriggers.OnSignal,
                    Signal = "domain_changes",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["payload"] = Json("\"{{signal.domain_changes.value}}\"")
                    },
                    OnOutcome = new Dictionary<string, StageOutcomeHandlerDefinition>
                    {
                        ["applied"] = new StageOutcomeHandlerDefinition
                        {
                            Effects =
                            [
                                new StageEffectDefinition
                                {
                                    Type = StageEffectTypes.SetFact,
                                    Fact = "processed",
                                    Value = Json("true")
                                }
                            ]
                        }
                    }
                }
            ],
            AdvanceWhenFacts = ["processed"]
        };
        return new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "processed", Type = "boolean", Source = "system", Scope = FactScopes.Request },
                new FactSchemaEntry { Key = "address", Type = "string", Source = "user", Scope = FactScopes.Request }
            ],
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "primary",
                    Type = FlowTypes.Primary,
                    Stages = [capture, new AgentFlowStage { Id = "done" }]
                }
            ]
        };
    }

    private static TurnPlan Plan() => new()
    {
        FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
        Signals =
        [
            new PlannedSignal
            {
                Type = "domain_changes",
                Value = Json("[\"uno\",\"dos\"]"),
                Evidence = "dos elementos"
            }
        ]
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class QueuePlanner : ITurnPlanner
    {
        private readonly Queue<TurnPlan> _plans;
        public QueuePlanner(params TurnPlan[] plans) => _plans = new Queue<TurnPlan>(plans);
        public Task<TurnPlanProposal> PlanAsync(TurnPlanningContext context, CancellationToken ct = default) =>
            Task.FromResult(new TurnPlanProposal(true, _plans.Dequeue(), [], 0, 0));
    }

    private sealed class StubPlanner : ITurnPlanner
    {
        private readonly TurnPlan _plan;
        public StubPlanner(TurnPlan plan) => _plan = plan;

        public Task<TurnPlanProposal> PlanAsync(TurnPlanningContext context, CancellationToken ct = default) =>
            Task.FromResult(new TurnPlanProposal(true, _plan, [], 0, 0));
    }

    private sealed class CapturingOperation : IAgentOperation
    {
        public const string Id = "generic.apply_changes";
        public JsonElement Input { get; private set; }
        public OperationDescriptor Descriptor { get; } = new(
            Id,
            "{\"type\":\"object\",\"required\":[\"payload\"]}",
            ["applied"],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default)
        {
            Input = input.Clone();
            return Task.FromResult(OperationOutcome.Ok("applied", new { }));
        }
    }

    private sealed class RecordingFactStore : IConversationFactsService
    {
        public List<IReadOnlyDictionary<string, string?>> Batches { get; } = [];

        public Task ApplyBatchAsync(Guid conversationId, Guid businessId, IReadOnlyDictionary<string, string?> mutations, IReadOnlySet<string> rememberAcrossRequests, CancellationToken ct = default)
        {
            if (mutations.Count > 0)
                Batches.Add(new Dictionary<string, string?>(mutations, StringComparer.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(Guid conversationId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid conversationId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetAsync(Guid conversationId, Guid businessId, string key, string value, bool rememberAcrossRequests = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ClearNonPersistentAsync(Guid conversationId, IReadOnlyCollection<string> persistentKeys, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ClearFieldsAsync(Guid conversationId, IReadOnlyCollection<string> fields, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
