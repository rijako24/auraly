using System.Text.Json;
using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Operations;
using Auraly.Platform.Application.Agents.Operations.Checkout;
using Auraly.Platform.Application.Agents.Planning;
using Auraly.Platform.Application.Agents.Templates;
using Auraly.Platform.Domain.Models;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class PaymentMethodsCapabilityTests
{
    [Fact]
    public void BuiltInCapability_IsAddedOnlyWhenCheckoutHasPaymentMethods()
    {
        var checkout = CheckoutWithMethods(
            ("cash", "efectivo al recibir"),
            ("card", "datafono al recibir"));

        var actions = BuiltInAgentCapabilities.AddPaymentMethodsAction([], checkout);
        var templates = BuiltInAgentCapabilities.AddPaymentMethodsTemplate(
            new Dictionary<string, string>(),
            checkout);

        var action = actions.Should().ContainSingle().Subject;
        action.Id.Should().Be(BuiltInAgentCapabilities.PaymentMethodsActionId);
        action.Signal.Type.Should().Be(BuiltInAgentCapabilities.PaymentMethodsSignalType);
        action.Actions.Should().ContainSingle(configured =>
            configured.Operation == BuiltInAgentCapabilities.PaymentMethodsOperationId
            && configured.Trigger == StageActionTriggers.OnSignal);
        templates.Should().ContainKey(BuiltInAgentCapabilities.PaymentMethodsTemplateId);

        BuiltInAgentCapabilities.AddPaymentMethodsAction([], new CheckoutDefinitions())
            .Should().BeEmpty();
        BuiltInAgentCapabilities.AddPaymentMethodsTemplate(
                new Dictionary<string, string>(),
                new CheckoutDefinitions())
            .Should().BeEmpty();
    }

    [Fact]
    public void BuiltInCapability_PreservesTenantTemplateOverride()
    {
        var checkout = CheckoutWithMethods(("cash", "efectivo"));
        var configured = new Dictionary<string, string>
        {
            [BuiltInAgentCapabilities.PaymentMethodsTemplateId] = "Formas aceptadas: {{#each payment_methods}}{{label}}{{/each}}"
        };

        var templates = BuiltInAgentCapabilities.AddPaymentMethodsTemplate(configured, checkout);

        templates[BuiltInAgentCapabilities.PaymentMethodsTemplateId]
            .Should().Be(configured[BuiltInAgentCapabilities.PaymentMethodsTemplateId]);
    }

    [Fact]
    public void BuiltInCapability_IsVisibleFromEveryStageThroughTurnScope()
    {
        var checkout = CheckoutWithMethods(("cash", "efectivo"));
        var stage = new AgentFlowStage { Id = "product_selection" };
        var config = new AgentConfig
        {
            Checkout = checkout,
            GlobalActions = BuiltInAgentCapabilities.AddPaymentMethodsAction([], checkout),
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "order",
                    Type = FlowTypes.Primary,
                    Stages = [stage, new AgentFlowStage { Id = "delivery" }]
                }
            ]
        };

        var scope = TurnPlanScopeBuilder.Build(
            config,
            stage,
            new Dictionary<string, string>());

        scope.Signals.Should().ContainKey(BuiltInAgentCapabilities.PaymentMethodsSignalType);
    }

    [Fact]
    public async Task Operation_ListsAndDeduplicatesConfiguredLabelsWithoutMutation()
    {
        var checkout = new CheckoutDefinitions
        {
            Modes = new Dictionary<string, CheckoutModeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["order"] = ModeWithMethods(
                    ("cash", "efectivo al recibir"),
                    ("card", "datafono al recibir")),
                ["reservation"] = ModeWithMethods(
                    ("cash", "Efectivo al recibir"),
                    ("transfer", "transferencia manual"))
            }
        };
        var operation = new ListPaymentMethodsOperation();
        using var input = JsonDocument.Parse("{}");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            new OperationContext
            {
                Config = new AgentConfig { Checkout = checkout },
                ConversationState = new ConversationState()
            });

        outcome.Success.Should().BeTrue();
        outcome.Code.Should().Be(BuiltInAgentCapabilities.PaymentMethodsListedOutcome);
        outcome.Effects.Should().BeEmpty();
        operation.Descriptor.MutationScopes.Should().BeEmpty();

        var presentation = outcome.Presentations.Should().ContainSingle().Subject;
        presentation.Mode.Should().Be(FragmentRenderMode.Exclusive);
        presentation.Priority.Should().Be(FragmentPriority.Required);
        var labels = ((IEnumerable<Dictionary<string, object?>>)presentation.Data["payment_methods"]!)
            .Select(item => item["label"]?.ToString())
            .ToArray();
        labels.Should().Equal(
            "efectivo al recibir",
            "datafono al recibir",
            "transferencia manual");
    }

    [Fact]
    public async Task Operation_FailsVisiblyWhenNoPaymentMethodsAreConfigured()
    {
        var operation = new ListPaymentMethodsOperation();
        using var input = JsonDocument.Parse("{}");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            new OperationContext
            {
                Config = new AgentConfig(),
                ConversationState = new ConversationState()
            });

        outcome.Success.Should().BeFalse();
        outcome.Code.Should().Be(BuiltInAgentCapabilities.PaymentMethodsNotConfiguredOutcome);
        outcome.Presentations.Should().BeEmpty();
    }

    private static CheckoutDefinitions CheckoutWithMethods(
        params (string Key, string Label)[] methods) => new()
    {
        Modes = new Dictionary<string, CheckoutModeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["order"] = ModeWithMethods(methods)
        }
    };

    private static CheckoutModeDefinition ModeWithMethods(
        params (string Key, string Label)[] methods) => new()
    {
        PaymentMethods = methods.ToDictionary(
            method => method.Key,
            method => new CheckoutPaymentMethodDefinition { Label = method.Label },
            StringComparer.OrdinalIgnoreCase)
    };
}
