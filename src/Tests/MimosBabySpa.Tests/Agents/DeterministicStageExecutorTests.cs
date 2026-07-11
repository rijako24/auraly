using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DeterministicStageExecutorTests
{
    [Fact]
    public async Task WhenReady_BindsRequiredFacts_OmitsMissingOptionalTime_AndAppliesOutcome()
    {
        var operation = new CapturingAvailabilityOperation();
        var executor = new DeterministicStageExecutor(
            new AgentOperationRegistry([operation]),
            new StageConditionEvaluator(),
            new OperationArgumentBinder());
        var context = Context();

        var result = await executor.ExecuteAsync(Stage(), StageActionTriggers.WhenReady, context);

        operation.CallCount.Should().Be(1);
        operation.LastInput.GetProperty("service").GetString().Should().Be("Corte infantil");
        operation.LastInput.GetProperty("date").GetString().Should().Be("2026-07-11");
        operation.LastInput.TryGetProperty("time", out _).Should().BeFalse();
        result.Presentations.Should().ContainSingle();
        result.Presentations[0].Mode.Should().Be(FragmentRenderMode.Exclusive);
        result.FactMutations["desired_date"].Should().Be("2026-07-11");
        result.Trace.Should().ContainSingle(value => !value.Skipped && value.OutcomeCode == "availability.options_available");
    }

    [Fact]
    public async Task SameActionAndInputs_IsIdempotentWithinExecutionScope()
    {
        var operation = new CapturingAvailabilityOperation();
        var executor = new DeterministicStageExecutor(
            new AgentOperationRegistry([operation]),
            new StageConditionEvaluator(),
            new OperationArgumentBinder());
        var context = Context();

        await executor.ExecuteAsync(Stage(), StageActionTriggers.WhenReady, context);
        var replay = await executor.ExecuteAsync(Stage(), StageActionTriggers.WhenReady, context);

        operation.CallCount.Should().Be(1);
        replay.Trace.Should().ContainSingle(value => value.Skipped && value.SkipReason == "idempotent_replay");
    }

    [Fact]
    public void ArgumentBinder_BindsSemanticSignalValueWithoutExposingAnOperationToTheLlm()
    {
        var binder = new OperationArgumentBinder();
        var context = Context();
        context = new DeterministicStageExecutionContext
        {
            Facts = context.Facts,
            OperationContext = context.OperationContext,
            Signals = [new SemanticSignal("service_selection", Json("corte infantil"), "corte infantil")]
        };
        var templates = new Dictionary<string, JsonElement>
        {
            ["text"] = Json("{{signal.service_selection.value}}")
        };

        var result = binder.Bind(templates, context);

        result.GetProperty("text").GetString().Should().Be("corte infantil");
    }
    [Fact]
    public async Task PresentationEffect_ConvertsOutcomeJsonRecursivelyForTemplates()
    {
        var operation = new CatalogOutcomeOperation();
        var executor = new DeterministicStageExecutor(
            new AgentOperationRegistry([operation]),
            new StageConditionEvaluator(),
            new OperationArgumentBinder());
        var stage = new AgentFlowStage
        {
            Id = "catalog",
            Actions =
            [
                new StageActionDefinition
                {
                    Id = "search",
                    Operation = operation.Descriptor.Id,
                    OnOutcome = new Dictionary<string, StageOutcomeHandlerDefinition>
                    {
                        ["products.found"] = new()
                        {
                            Effects =
                            [
                                new StageEffectDefinition
                                {
                                    Type = StageEffectTypes.AddPresentation,
                                    Template = "catalog_results",
                                    Mode = "Exclusive",
                                    Priority = "Required"
                                }
                            ]
                        }
                    }
                }
            ]
        };

        var result = await executor.ExecuteAsync(stage, StageActionTriggers.WhenReady, Context());

        var presentation = result.Presentations.Should().ContainSingle().Subject;
        var products = presentation.Data["products"].Should().BeAssignableTo<IReadOnlyList<object?>>().Subject;
        var first = products.Should().ContainSingle().Subject.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        first["name"].Should().Be("Pollo");
        first["unit_price"].Should().Be(12000m);
    }
    [Fact]
    public async Task PresentationEffect_CanUseTypedOperationErrorContext()
    {
        var operation = new AmbiguousOutcomeOperation();
        var executor = new DeterministicStageExecutor(
            new AgentOperationRegistry([operation]),
            new StageConditionEvaluator(),
            new OperationArgumentBinder());
        var stage = new AgentFlowStage
        {
            Id = "cart",
            Actions =
            [
                new StageActionDefinition
                {
                    Id = "apply",
                    Operation = operation.Descriptor.Id,
                    OnOutcome = new Dictionary<string, StageOutcomeHandlerDefinition>
                    {
                        ["cart.product_ambiguous"] = new()
                        {
                            Effects =
                            [
                                new StageEffectDefinition
                                {
                                    Type = StageEffectTypes.AddPresentation,
                                    Template = "product_ambiguity",
                                    DataPath = "error.context",
                                    Mode = "Exclusive",
                                    Priority = "Required"
                                }
                            ]
                        }
                    }
                }
            ]
        };

        var result = await executor.ExecuteAsync(stage, StageActionTriggers.WhenReady, Context());

        var presentation = result.Presentations.Should().ContainSingle().Subject;
        presentation.Data["product_text"].Should().Be("pollo");
        presentation.Data["product_options"].Should().BeAssignableTo<IReadOnlyList<object?>>();
    }
    [Fact]
    public async Task RecoverableFailure_RetriesUpToConfiguredMaximum()
    {
        var operation = new RecoverableThenSuccessfulOperation();
        var executor = new DeterministicStageExecutor(
            new AgentOperationRegistry([operation]),
            new StageConditionEvaluator(),
            new OperationArgumentBinder());
        var stage = new AgentFlowStage
        {
            Id = "retry",
            Actions =
            [
                new StageActionDefinition
                {
                    Id = "retryable",
                    Operation = operation.Descriptor.Id,
                    Execution = new StageActionExecutionDefinition { MaxAttempts = 2, TimeoutSeconds = 5 },
                    OnOutcome = new Dictionary<string, StageOutcomeHandlerDefinition>
                    {
                        ["completed"] = new()
                    }
                }
            ]
        };

        var result = await executor.ExecuteAsync(stage, StageActionTriggers.WhenReady, Context());

        operation.CallCount.Should().Be(2);
        result.Trace.Should().ContainSingle(value => value.OutcomeCode == "completed" && value.Success);
    }
    private static AgentFlowStage Stage() => new()
    {
        Id = "scheduling",
        Actions =
        [
            new StageActionDefinition
            {
                Id = "check_availability",
                Operation = "reservation.check_availability",
                Arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["service"] = Json("{{fact.service}}"),
                    ["date"] = Json("{{fact.desired_date}}"),
                    ["time"] = Json("{{fact.desired_time}}")
                },
                OnOutcome = new Dictionary<string, StageOutcomeHandlerDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["availability.options_available"] = new StageOutcomeHandlerDefinition
                    {
                        Effects =
                        [
                            new StageEffectDefinition
                            {
                                Type = StageEffectTypes.SetFactsFromOutcome,
                                Bindings = new Dictionary<string, string>
                                {
                                    ["desired_date"] = "canonicalDate"
                                }
                            }
                        ]
                    }
                }
            }
        ]
    };

    private static DeterministicStageExecutionContext Context() => new()
    {
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Corte infantil",
            ["desired_date"] = "2026-07-11"
        },
        OperationContext = new OperationContext
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            BusinessToday = new DateOnly(2026, 7, 10),
            BusinessNow = DateTimeOffset.UtcNow,
            Config = new AgentConfig()
        }
    };

    private static JsonElement Json(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private sealed class CatalogOutcomeOperation : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "test.catalog",
            "{\"type\":\"object\",\"required\":[]}",
            ["products.found"], [], [], []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok("products.found", new
            {
                products = new[] { new { name = "Pollo", unit_price = 12000m } }
            }));
    }
    private sealed class AmbiguousOutcomeOperation : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "test.ambiguous",
            "{\"type\":\"object\",\"required\":[]}",
            ["cart.product_ambiguous"], [], [], []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Fail(
                "cart.product_ambiguous",
                "ambiguous",
                true,
                context: new
                {
                    product_text = "pollo",
                    product_options = new[] { new { Name = "Pechuga", UnitPrice = 12000m, Currency = "COP" } }
                }));
    }
    private sealed class RecoverableThenSuccessfulOperation : IAgentOperation
    {
        public int CallCount { get; private set; }
        public OperationDescriptor Descriptor { get; } = new(
            "test.retryable",
            "{\"type\":\"object\",\"required\":[]}",
            ["temporary", "completed"], [], [], []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CallCount == 1
                ? OperationOutcome.Fail("temporary", "retry", true)
                : OperationOutcome.Ok("completed", new { }));
        }
    }
    private sealed class CapturingAvailabilityOperation : IAgentOperation
    {
        public int CallCount { get; private set; }
        public JsonElement LastInput { get; private set; }

        public OperationDescriptor Descriptor { get; } = new(
            "reservation.check_availability",
            "{\"type\":\"object\",\"required\":[\"service\",\"date\"]}",
            ["availability.options_available"],
            [],
            ["availability_slots"],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastInput = input.Clone();
            return Task.FromResult(OperationOutcome.Ok(
                "availability.options_available",
                new { canonicalDate = "2026-07-11" },
                [
                    new OperationPresentation(
                        "availability_slots",
                        new Dictionary<string, object?> { ["options"] = new[] { "9:00 AM" } },
                        FragmentRenderMode.Exclusive,
                        FragmentPriority.Required)
                ]));
        }
    }
}
