using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Planning;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class TurnPlanFailSoftRecoveryTests
{
    [Fact]
    public void Recovery_DropsOnlyInvalidFact_AndPreservesIndependentMeaning()
    {
        var scope = Scope(
            facts:
            [
                Fact("customer_name"),
                Fact("delivery_address")
            ],
            signals:
            [
                Signal("recipe_request", """{"type":"string"}""")
            ]);
        var plan = new TurnPlan
        {
            FlowIntent = Flow("order"),
            Facts =
            [
                Claim("customer_name", "Richard", "richard"),
                Claim("delivery_address", "calle falsa", "evidencia inventada")
            ],
            Signals =
            [
                new PlannedSignal
                {
                    Type = "recipe_request",
                    Value = JsonSerializer.SerializeToElement("cerdo"),
                    Evidence = "preparar cerdo"
                }
            ]
        };
        var validator = new TurnPlanValidator();
        var validation = validator.Validate(plan, scope, "richard quiero preparar cerdo");

        var recovered = TurnPlanFailSoftRecovery.TryRecover(
            plan, validation, scope, out var result);

        recovered.Should().BeTrue();
        result.Facts.Should().ContainSingle(fact => fact.Key == "customer_name");
        result.Facts.Should().NotContain(fact => fact.Key == "delivery_address");
        result.Signals.Should().ContainSingle(signal => signal.Type == "recipe_request");
        validator.Validate(result, scope, "richard quiero preparar cerdo").IsValid.Should().BeTrue();
    }

    [Fact]
    public void Recovery_RefusesPlanWithFatalDuplicateClaim()
    {
        var scope = Scope(facts: [Fact("customer_name")], signals: []);
        var plan = new TurnPlan
        {
            FlowIntent = Flow("order"),
            Facts =
            [
                Claim("customer_name", "Richard", "richard"),
                Claim("customer_name", "Ricardo", "richard")
            ]
        };
        var validation = new TurnPlanValidator().Validate(plan, scope, "richard");

        TurnPlanFailSoftRecovery.TryRecover(plan, validation, scope, out _).Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == "fact.duplicate"
            && issue.RecoveryAction == TurnPlanRecoveryAction.None);
    }

    [Fact]
    public void Recovery_ConvertsUnresolvedSelectorIntoScopedClarification()
    {
        var customerType = new FactSchemaEntry
        {
            Key = "customer_type",
            Type = "string",
            Source = "user",
            Options = [new FactValueOption { Value = "Hogar", Label = "Hogar", Selector = "A" }]
        };
        var scope = Scope(facts: [customerType], signals: []);
        var plan = new TurnPlan { FlowIntent = Flow("order") };
        var validator = new TurnPlanValidator();
        var selector = new OptionSelectorReference(customerType, customerType.Options.Single());
        var validation = validator.Validate(plan, scope, "la a", selector);

        TurnPlanFailSoftRecovery.TryRecover(plan, validation, scope, out var result).Should().BeTrue();

        result.Response.Mode.Should().Be("ask_clarification");
        result.Response.AmbiguousFields.Should().Equal("customer_type");
        validator.Validate(result, scope, "la a", selector).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Recovery_FallsBackOnlyToPrimaryFlowDeclaredByCurrentScope()
    {
        var scope = Scope(facts: [], signals: []);
        scope = scope with
        {
            Flows = new Dictionary<string, TurnPlanFlowOption>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant_primary"] = new("tenant_primary", "primary", ""),
                ["tenant_secondary"] = new("tenant_secondary", "secondary", "")
            },
            PrimaryFlowId = "tenant_primary"
        };
        var plan = new TurnPlan
        {
            FlowIntent = new PlannedFlowIntent
            {
                CandidateFlow = "tenant_secondary",
                Confidence = 0.9,
                Evidence = "evidencia ausente"
            }
        };
        var validator = new TurnPlanValidator();
        var validation = validator.Validate(plan, scope, "hola");

        TurnPlanFailSoftRecovery.TryRecover(plan, validation, scope, out var result).Should().BeTrue();

        result.FlowIntent.CandidateFlow.Should().Be("tenant_primary");
        result.FlowIntent.Evidence.Should().BeNull();
        validator.Validate(result, scope, "hola").IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("delivery_address")]
    [InlineData("tenant_custom_location")]
    public void Recovery_UsesDeclarativeFactKeysFromScope(string factKey)
    {
        var scope = Scope(facts: [Fact(factKey)], signals: []);
        var plan = new TurnPlan
        {
            FlowIntent = Flow("order"),
            Facts = [Claim(factKey, "inventado", "evidencia ausente")]
        };
        var validation = new TurnPlanValidator().Validate(plan, scope, "hola");

        TurnPlanFailSoftRecovery.TryRecover(plan, validation, scope, out var result).Should().BeTrue();

        result.Facts.Should().BeEmpty();
    }

    private static TurnPlanScope Scope(
        IReadOnlyList<FactSchemaEntry> facts,
        IReadOnlyList<StageSignalDefinition> signals) =>
        new(
            facts.ToDictionary(fact => fact.Key, StringComparer.OrdinalIgnoreCase),
            signals.ToDictionary(signal => signal.Type, StringComparer.OrdinalIgnoreCase))
        {
            PrimaryFlowId = "order",
            Flows = new Dictionary<string, TurnPlanFlowOption>(StringComparer.OrdinalIgnoreCase)
            {
                ["order"] = new("order", "primary", "")
            }
        };

    private static FactSchemaEntry Fact(string key) => new()
    {
        Key = key,
        Type = "string",
        Source = "user"
    };

    private static StageSignalDefinition Signal(string type, string schema) => new()
    {
        Type = type,
        ValueSchema = JsonDocument.Parse(schema).RootElement.Clone()
    };

    private static PlannedFactClaim Claim(string key, string value, string evidence) => new()
    {
        Key = key,
        Operation = TurnPlanOperations.Set,
        Value = JsonSerializer.SerializeToElement(value),
        Evidence = evidence
    };

    private static PlannedFlowIntent Flow(string id) => new()
    {
        CandidateFlow = id,
        Confidence = 1
    };
}
