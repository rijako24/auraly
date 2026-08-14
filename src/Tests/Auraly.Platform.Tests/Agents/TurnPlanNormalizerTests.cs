using Xunit;
using System.Text.Json;
using FluentAssertions;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Planning;

namespace Auraly.Platform.Tests.Agents;

public sealed class TurnPlanNormalizerTests
{
    [Fact]
    public void Normalize_CanonicalizesPhoneFacts()
    {
        var scope = Scope(
            facts: [new FactSchemaEntry { Key = "phone", Type = "phone", Source = "user" }],
            signals: []);
        var plan = Plan(
            facts: [new PlannedFactClaim
            {
                Key = "phone",
                Value = JsonSerializer.SerializeToElement("+57 300 123 4567"),
                Evidence = "+57 300 123 4567"
            }],
            signals: []);

        var normalized = TurnPlanNormalizer.Normalize(plan, scope);

        normalized.Facts.Single().Value.GetString().Should().Be("+573001234567");
    }

    [Fact]
    public void Normalize_DistinctSignalValues_ProduceConfiguredAmbiguityWithoutDroppingSignal()
    {
        var signal = new StageSignalDefinition
        {
            Type = "changes",
            ValueSchema = JsonDocument.Parse("""{"type":"array"}""").RootElement.Clone(),
            AmbiguityRules =
            [
                new SignalAmbiguityRuleDefinition
                {
                    ValueProperty = "destination",
                    Field = "address",
                    MinimumDistinctValues = 2
                }
            ]
        };
        var scope = Scope(
            facts: [new FactSchemaEntry { Key = "address", Type = "string", Source = "user" }],
            signals: [signal]);
        var plan = Plan(
            facts: [],
            signals:
            [
                new PlannedSignal
                {
                    Type = "changes",
                    Value = JsonDocument.Parse(
                        """[{"destination":"A"},{"destination":"B"}]""").RootElement.Clone(),
                    Evidence = "A y B"
                }
            ]);

        var normalized = TurnPlanNormalizer.Normalize(plan, scope);

        normalized.Response.Mode.Should().Be("ask_clarification");
        normalized.Response.AmbiguousFields.Should().ContainSingle().Which.Should().Be("address");
        normalized.Signals.Should().ContainSingle().Which.Type.Should().Be("changes");
    }

    private static TurnPlan Plan(
        IReadOnlyList<PlannedFactClaim> facts,
        IReadOnlyList<PlannedSignal> signals) => new()
    {
        FlowIntent = new PlannedFlowIntent { CandidateFlow = "primary", Confidence = 1 },
        Facts = facts,
        Signals = signals,
        Response = new TurnPlanResponseDirective()
    };

    private static TurnPlanScope Scope(
        IReadOnlyList<FactSchemaEntry> facts,
        IReadOnlyList<StageSignalDefinition> signals) => new(
            facts.ToDictionary(value => value.Key, StringComparer.OrdinalIgnoreCase),
            signals.ToDictionary(value => value.Type, StringComparer.OrdinalIgnoreCase))
        {
            PrimaryFlowId = "primary",
            Flows = new Dictionary<string, TurnPlanFlowOption>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary"] = new("primary", "primary", "")
            },
            Stages =
            [
                new TurnPlanStageOption(
                    "primary", "stage", "", "", facts.Select(value => value.Key).ToArray(),
                    facts.Select(value => value.Key).ToArray(), signals.Select(value => value.Type).ToArray(), true)
            ]
        };
}
