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
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DeterministicTurnCoordinatorTests
{
    [Fact]
    public void ReconcileKnownFactAmbiguities_UsesHydratedFactsButPreservesExplicitCorrections()
    {
        var knownFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_name"] = "Richard"
        };
        var config = new AgentConfig
        {
            FactSchema = [new FactSchemaEntry { Key = "customer_name", Type = "string" }]
        };
        var spuriousAmbiguity = new TurnPlan
        {
            Response = new TurnPlanResponseDirective
            {
                Mode = "ask_clarification", AmbiguousFields = ["customer_name"]
            }
        };

        var reconciled = DeterministicTurnCoordinator.ReconcileKnownFactAmbiguities(
            config, spuriousAmbiguity, knownFacts);

        reconciled.Response.Mode.Should().Be("continue");
        reconciled.Response.AmbiguousFields.Should().BeEmpty();

        var explicitCorrection = new TurnPlan
        {
            Facts =
            [
                new PlannedFactClaim
                {
                    Key = "customer_name", Operation = TurnPlanOperations.Clear,
                    Value = Json("null"), Evidence = "ese no es mi nombre"
                }
            ],
            Response = new TurnPlanResponseDirective
            {
                Mode = "ask_clarification", AmbiguousFields = ["customer_name"]
            }
        };

        var preserved = DeterministicTurnCoordinator.ReconcileKnownFactAmbiguities(
            config, explicitCorrection, knownFacts);

        preserved.Response.Mode.Should().Be("ask_clarification");
        preserved.Response.AmbiguousFields.Should().Equal("customer_name");
    }


    [Fact]
    public async Task Execute_RequestResetEffect_ReentersFirstStageAndDropsOnlyClearedFacts()
    {
        var resetOperation = new ResetEffectOperation(["order_finalized"]);
        var coordinator = new DeterministicTurnCoordinator(
            new StubPlanner(new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "order", Confidence = 1 },
                Signals =
                [
                    new PlannedSignal
                    {
                        Type = "restart_request", Value = Json("true"),
                        Evidence = "quiero hacer un pedido nuevo", Confidence = 1
                    }
                ]
            }),
            new DeterministicFlowSelector(),
            new FactMutationBatchProcessor(),
            new RecordingFactStore(),
            new ConversationVerificationService(),
            new DeterministicStageExecutor(
                new AgentOperationRegistry([resetOperation]),
                new StageConditionEvaluator(),
                new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "customer_name", Type = "string", Scope = FactScopes.Customer },
                new FactSchemaEntry { Key = "order_finalized", Type = "boolean", Scope = FactScopes.Request }
            ],
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "restart",
                    Signal = new StageSignalDefinition { Type = "restart_request", ValueSchema = Json("{\"type\":\"boolean\"}") },
                    Actions =
                    [
                        new StageActionDefinition
                        {
                            Id = "reset", Operation = resetOperation.Descriptor.Id,
                            Trigger = StageActionTriggers.OnSignal, Signal = "restart_request"
                        }
                    ]
                }
            ],
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "order", Type = FlowTypes.Primary,
                    Stages =
                    [
                        new AgentFlowStage { Id = "start", Collect = ["customer_name"] },
                        new AgentFlowStage { Id = "delivery", Collect = ["order_finalized"] }
                    ]
                }
            ]
        };
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_name"] = "Richard", ["order_finalized"] = "true"
        };

        var result = await coordinator.ExecuteAsync(new DeterministicTurnRequest
        {
            Config = config,
            OperationContext = Context(config, new ConversationState()),
            CurrentFacts = facts,
            CurrentFlowId = "order", ActiveFlowId = "order", CurrentStageId = "delivery",
            LatestUserMessage = "quiero hacer un pedido nuevo"
        });

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.VisitedStages.Should().StartWith("start");
        result.CurrentStageId.Should().Be("start");
        result.Facts.Should().Contain("customer_name", "Richard");
        result.Facts.Should().NotContainKey("order_finalized");
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("delivery")]
    public async Task Execute_GlobalSignal_UsesStageOverrideOtherwiseGlobalFallbackExactlyOnce(string currentStage)
    {
        var operation = new CapturingOperation();
        var plan = new TurnPlan
        {
            FlowIntent = new PlannedFlowIntent { CandidateFlow = "order", Confidence = 1 },
            Signals =
            [
                new PlannedSignal
                {
                    Type = "catalog_query",
                    Value = Json("{\"queries\":[\"pechuga\"]}"),
                    Evidence = "que pechugas tienes",
                    Confidence = 1
                }
            ]
        };
        var coordinator = new DeterministicTurnCoordinator(
            new StubPlanner(plan),
            new DeterministicFlowSelector(),
            new FactMutationBatchProcessor(),
            new RecordingFactStore(),
            new ConversationVerificationService(),
            new DeterministicStageExecutor(
                new AgentOperationRegistry([operation]),
                new StageConditionEvaluator(),
                new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));
        var config = new AgentConfig
        {
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "catalog_lookup",
                    Signal = new StageSignalDefinition
                    {
                        Type = "catalog_query",
                        ValueSchema = Json("{\"type\":\"object\"}")
                    },
                    Actions =
                    [
                        new StageActionDefinition
                        {
                            Id = "search_catalog",
                            Operation = CapturingOperation.Id,
                            Trigger = StageActionTriggers.OnSignal,
                            Signal = "catalog_query",
                            Arguments = new Dictionary<string, JsonElement>
                            {
                                ["payload"] = Json("\"{{turn.message}}\"")
                            }
                        }
                    ]
                }
            ],
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "order",
                    Type = FlowTypes.Primary,
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "selection",
                            Signals =
                            [
                                new StageSignalDefinition
                                {
                                    Type = "catalog_query",
                                    ValueSchema = Json("{\"type\":\"object\"}")
                                }
                            ],
                            Actions =
                            [
                                new StageActionDefinition
                                {
                                    Id = "stage_catalog",
                                    Operation = CapturingOperation.Id,
                                    Trigger = StageActionTriggers.OnSignal,
                                    Signal = "catalog_query",
                                    Arguments = new Dictionary<string, JsonElement>
                                    {
                                        ["payload"] = Json("\"{{turn.message}}\"")
                                    }
                                }
                            ]
                        },
                        new AgentFlowStage { Id = "delivery" }
                    ]
                }
            ]
        };

        var result = await coordinator.ExecuteAsync(new DeterministicTurnRequest
        {
            Config = config,
            OperationContext = Context(config, new ConversationState()),
            CurrentFacts = new Dictionary<string, string>(),
            CurrentFlowId = "order",
            ActiveFlowId = "order",
            CurrentStageId = currentStage,
            LatestUserMessage = "que pechugas tienes"
        });

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        operation.CallCount.Should().Be(1);
        var trace = result.Trace.Should()
            .ContainSingle(value => value.OperationId == CapturingOperation.Id).Subject;
        trace.ActionId.Should().Be(currentStage == "selection" ? "stage_catalog" : "search_catalog");
    }

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
        var session = new AgentConversationContext();

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
                ConversationState = new ConversationState(),
                Session = session
            },
            CurrentFlowId = "primary",
            CurrentStageId = "capture",
            LatestUserMessage = "dos elementos"
        });

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.VisitedStages.Should().Equal("capture", "done");
        result.Facts["processed"].Should().Be("true");
        operation.Input.GetProperty("payload").GetArrayLength().Should().Be(2);
        operation.ReceivedSession.Should().BeSameAs(session);
        operation.ReceivedSession!.Facts["address"].Should().Be("calle 5");
        factStore.Batches.Count(batch =>
            batch.TryGetValue("processed", out var value) && value == "true").Should().Be(1);
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

    [Fact]
    public async Task Execute_SwitchesAwayFromPendingPlan_OnExplicitFlowEvidenceRegardlessOfConfidence()
    {
        var planner = new QueuePlanner(
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
                Response = new TurnPlanResponseDirective
                {
                    Mode = "ask_clarification",
                    AmbiguousFields = ["address"]
                }
            },
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent
                {
                    CandidateFlow = "reservation_management",
                    Confidence = 0.1,
                    Evidence = "cambiar reserva"
                }
            });
        var factStore = new RecordingFactStore();
        var coordinator = new DeterministicTurnCoordinator(
            planner,
            new DeterministicFlowSelector(),
            new FactMutationBatchProcessor(),
            factStore,
            new ConversationVerificationService(),
            new DeterministicStageExecutor(
                new AgentOperationRegistry([]),
                new StageConditionEvaluator(),
                new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));
        var config = new AgentConfig
        {
            FactSchema = [new FactSchemaEntry { Key = "address", Type = "string", Source = "user" }],
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "primary",
                    Type = FlowTypes.Primary,
                    Stages = [new AgentFlowStage { Id = "capture", Collect = ["address"] }]
                },
                new AgentFlowDefinition
                {
                    Id = "reservation_management",
                    Type = FlowTypes.Secondary,
                    Stages = [new AgentFlowStage { Id = "manage" }]
                }
            ]
        };
        var state = new ConversationState();
        var context = new OperationContext
        {
            BusinessNow = DateTimeOffset.Parse("2026-07-11T10:00:00-05:00"),
            Config = config,
            ConversationState = state
        };

        var first = await coordinator.ExecuteAsync(Request(
            config, context, new Dictionary<string, string>(), new Dictionary<string, long>()));
        first.Success.Should().BeTrue();
        state.PendingTurnPlan.Should().NotBeNull();

        var second = await coordinator.ExecuteAsync(Request(
            config, context, first.Facts, first.FactVersions, "quiero cambiar reserva"));

        second.Success.Should().BeTrue(string.Join("; ", second.Errors));
        second.Route!.ActiveFlowId.Should().Be("reservation_management");
        state.PendingTurnPlan.Should().BeNull();
    }
    [Fact]
    public async Task Execute_ProcessesIndependentFactWhileAnotherFieldRemainsPending()
    {
        var planner = new QueuePlanner(
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
                Response = new TurnPlanResponseDirective
                {
                    Mode = "ask_clarification",
                    AmbiguousFields = ["customer_type"]
                }
            },
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
                Facts =
                [
                    new PlannedFactClaim
                    {
                        Key = "customer_name",
                        Operation = TurnPlanOperations.Set,
                        Value = Json("\"Richard\""),
                        Evidence = "Richard"
                    }
                ]
            },
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
                Facts =
                [
                    new PlannedFactClaim
                    {
                        Key = "customer_type",
                        Operation = TurnPlanOperations.Set,
                        Value = Json("\"Hogar\""),
                        Evidence = "la a"
                    }
                ]
            });
        var factStore = new RecordingFactStore();
        var coordinator = new DeterministicTurnCoordinator(
            planner,
            new DeterministicFlowSelector(),
            new FactMutationBatchProcessor(),
            factStore,
            new ConversationVerificationService(),
            new DeterministicStageExecutor(
                new AgentOperationRegistry([]),
                new StageConditionEvaluator(),
                new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));
        var customerNameStage = new AgentFlowStage
        {
            Id = "customer_name",
            Collect = ["customer_name"],
            AdvanceWhenFacts = ["customer_name"],
            Response = new StageResponseDefinition { FallbackTemplate = "customer_name_prompt" }
        };
        var customerTypeStage = new AgentFlowStage
        {
            Id = "customer_type",
            Collect = ["customer_type"],
            AdvanceWhenFacts = ["customer_type"],
            Response = new StageResponseDefinition { FallbackTemplate = "customer_type_prompt" }
        };
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "customer_name", Type = "string", Source = "user" },
                new FactSchemaEntry { Key = "customer_type", Type = "string", Source = "user" }
            ],
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "primary",
                    Type = FlowTypes.Primary,
                    Stages = [customerNameStage, customerTypeStage, new AgentFlowStage { Id = "done" }]
                }
            ]
        };
        var state = new ConversationState();
        var operationContext = new OperationContext
        {
            BusinessNow = DateTimeOffset.UtcNow,
            Config = config,
            ConversationState = state
        };

        DeterministicTurnRequest Turn(
            IReadOnlyDictionary<string, string> facts,
            IReadOnlyDictionary<string, long> versions,
            string stage,
            string message) => new()
            {
                Config = config,
                OperationContext = operationContext,
                CurrentFacts = facts,
                FactVersions = versions,
                CurrentFlowId = "primary",
                ActiveFlowId = "primary",
                CurrentStageId = stage,
                LatestUserMessage = message
            };

        var first = await coordinator.ExecuteAsync(Turn(
            new Dictionary<string, string>(),
            new Dictionary<string, long>(),
            "customer_name",
            "La a"));
        first.CurrentStageId.Should().Be("customer_name");
        state.PendingTurnPlan.Should().NotBeNull();

        var second = await coordinator.ExecuteAsync(Turn(
            first.Facts,
            first.FactVersions,
            first.CurrentStageId!,
            "Richard"));
        second.Facts["customer_name"].Should().Be("Richard");
        second.CurrentStageId.Should().Be("customer_type");
        second.Response!.FallbackTemplate.Should().Be("customer_type_prompt");
        state.PendingTurnPlan.Should().NotBeNull();

        var third = await coordinator.ExecuteAsync(Turn(
            second.Facts,
            second.FactVersions,
            second.CurrentStageId!,
            "la a"));
        third.Facts["customer_name"].Should().Be("Richard");
        third.Facts["customer_type"].Should().Be("Hogar");
        state.PendingTurnPlan.Should().BeNull();
    }

    [Fact]
    public async Task Execute_AccumulatesNewAmbiguityWithoutReplacingExistingPendingPlan()
    {
        var planner = new QueuePlanner(
            ClarificationPlan("address"),
            ClarificationPlan("phone"));
        var coordinator = Coordinator(planner);
        var config = ClarificationConfig();
        var state = new ConversationState();
        var context = Context(config, state);

        var first = await coordinator.ExecuteAsync(ClarificationRequest(
            config, context, new Dictionary<string, string>(), new Dictionary<string, long>()));
        var second = await coordinator.ExecuteAsync(ClarificationRequest(
            config, context, first.Facts, first.FactVersions));

        state.PendingTurnPlan.Should().NotBeNull();
        state.PendingTurnPlan!.AmbiguousFields.Should().BeEquivalentTo("address", "phone");
        TurnPlanParser.TryParse(state.PendingTurnPlan.PlanJson, out var pendingPlan, out _).Should().BeTrue();
        pendingPlan!.Response.AmbiguousFields.Should().BeEquivalentTo("address", "phone");
    }

    [Fact]
    public async Task Execute_AccumulatesPartialResolutionAndAsksOnlyForRemainingField()
    {
        var planner = new QueuePlanner(
            new TurnPlan
            {
                FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
                Response = new TurnPlanResponseDirective
                {
                    Mode = "ask_clarification",
                    AmbiguousFields = ["address", "phone"]
                }
            },
            FactPlan("address", "Calle 10"),
            FactPlan("phone", "3001234567"));
        var coordinator = Coordinator(planner);
        var config = ClarificationConfig();
        var state = new ConversationState();
        var context = Context(config, state);

        var first = await coordinator.ExecuteAsync(ClarificationRequest(
            config, context, new Dictionary<string, string>(), new Dictionary<string, long>()));
        var second = await coordinator.ExecuteAsync(ClarificationRequest(
            config, context, first.Facts, first.FactVersions));

        second.Facts.Should().NotContainKey("address");
        state.PendingTurnPlan.Should().NotBeNull();
        state.PendingTurnPlan!.AmbiguousFields.Should().Equal("phone");
        TurnPlanParser.TryParse(state.PendingTurnPlan.PlanJson, out var partialPlan, out _).Should().BeTrue();
        partialPlan!.Facts.Should().ContainSingle(fact => fact.Key == "address");

        var third = await coordinator.ExecuteAsync(ClarificationRequest(
            config, context, second.Facts, second.FactVersions));

        third.Facts["address"].Should().Be("Calle 10");
        third.Facts["phone"].Should().Be("3001234567");
        state.PendingTurnPlan.Should().BeNull();
    }

    [Fact]
    public void ProtectPendingCommerceSelection_DefersFinalizationAndSynthesizesConfiguredCartSignal()
    {
        var config = new AgentConfig
        {
            Commerce = new CommerceConfig { Enabled = true },
            FactSchema = [new FactSchemaEntry { Key = "done", Role = "order.finalized", Type = "boolean" }],
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "order",
                    Type = FlowTypes.Primary,
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "products",
                            Signals = [new StageSignalDefinition { Type = "cart_mutation", ValueSchema = Json("{\"type\":\"array\"}") }],
                            Actions =
                            [
                                new StageActionDefinition
                                {
                                    Id = "apply",
                                    Operation = "commerce.apply_order_changes",
                                    Signal = "cart_mutation"
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        var facts = new Dictionary<string, string>
        {
            ["system.pending_cart_commands"] = """
                {"schemaVersion":1,"commands":[{"operation":"add","productText":"pernil","quantity":1,"destinationReference":null}],"ambiguousProductText":"pernil","productCandidates":[{"name":"PERNIL A","unitPrice":1,"currency":"COP"},{"name":"PERNIL B","unitPrice":2,"currency":"COP"}],"expiresAtUtc":"2099-01-01T00:00:00Z"}
                """
        };
        var plan = new TurnPlan
        {
            Signals =
            [
                new PlannedSignal
                {
                    Type = "cart_mutation",
                    Value = Json("[{\"operation\":\"cancel_pending\",\"productText\":\"pernil\",\"quantity\":null,\"destinationReference\":null}]"),
                    Evidence = "eso es todo"
                }
            ],
            Facts =
            [
                new PlannedFactClaim
                {
                    Key = "done",
                    Operation = TurnPlanOperations.Set,
                    Value = Json("true"),
                    Evidence = "eso es todo"
                }
            ]
        };

        var protectedPlan = DeterministicTurnCoordinator.ProtectPendingCommerceSelection(
            config, facts, "eso es todo", plan);

        protectedPlan.Facts.Should().BeEmpty();
        protectedPlan.Signals.Should().ContainSingle(signal =>
            signal.Type == "cart_mutation" && signal.Value.ValueKind == JsonValueKind.Array
            && signal.Value.GetArrayLength() == 0);
    }
    private static DeterministicTurnCoordinator Coordinator(ITurnPlanner planner) =>
        new(
            planner,
            new DeterministicFlowSelector(),
            new FactMutationBatchProcessor(),
            new RecordingFactStore(),
            new ConversationVerificationService(),
            new DeterministicStageExecutor(
                new AgentOperationRegistry([]),
                new StageConditionEvaluator(),
                new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));

    private static AgentConfig ClarificationConfig() => new()
    {
        FactSchema =
        [
            new FactSchemaEntry { Key = "address", Type = "string", Source = "user" },
            new FactSchemaEntry { Key = "phone", Type = "string", Source = "user" }
        ],
        Flows =
        [
            new AgentFlowDefinition
            {
                Id = "primary",
                Type = FlowTypes.Primary,
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "capture",
                        Collect = ["address", "phone"]
                    }
                ]
            }
        ]
    };

    private static OperationContext Context(AgentConfig config, ConversationState state) => new()
    {
        BusinessNow = DateTimeOffset.UtcNow,
        Config = config,
        ConversationState = state
    };

    private static DeterministicTurnRequest ClarificationRequest(
        AgentConfig config,
        OperationContext context,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyDictionary<string, long> versions) => new()
        {
            Config = config,
            OperationContext = context,
            CurrentFacts = facts,
            FactVersions = versions,
            CurrentFlowId = "primary",
            ActiveFlowId = "primary",
            CurrentStageId = "capture",
            LatestUserMessage = "respuesta"
        };

    private static TurnPlan ClarificationPlan(string field) => new()
    {
        FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
        Response = new TurnPlanResponseDirective
        {
            Mode = "ask_clarification",
            AmbiguousFields = [field]
        }
    };

    private static TurnPlan FactPlan(string key, string value) => new()
    {
        FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
        Facts =
        [
            new PlannedFactClaim
            {
                Key = key,
                Operation = TurnPlanOperations.Set,
                Value = Json(JsonSerializer.Serialize(value)),
                Evidence = value
            }
        ]
    };

    private static DeterministicTurnRequest Request(
        AgentConfig config,
        OperationContext context,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyDictionary<string, long> versions,
        string latestUserMessage = "mensaje") => new()
    {
        Config = config, OperationContext = context, CurrentFacts = facts, FactVersions = versions,
        CurrentFlowId = "primary", CurrentStageId = "capture", ActiveFlowId = "primary",
        LatestUserMessage = latestUserMessage
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
        Facts = [new PlannedFactClaim
        {
            Key = "address",
            Operation = TurnPlanOperations.Set,
            Value = Json("\"calle 5\""),
            Evidence = "dos elementos"
        }],
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
        public AgentConversationContext? ReceivedSession { get; private set; }
        public int CallCount { get; private set; }
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
            CallCount++;
            ReceivedSession = context.Session;
            return Task.FromResult(OperationOutcome.Ok("applied", new { }));
        }
    }

    private sealed class ResetEffectOperation(IReadOnlyList<string> clearedFacts) : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "test.reset_request",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{}}",
            ["conversation.request_reset"], [], [], []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input, OperationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok(
                "conversation.request_reset",
                new { reset = true },
                effects: [new ResetRequestOperationEffect(clearedFacts)]));
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
