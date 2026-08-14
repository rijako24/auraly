using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Operations;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class AgentConfigurationExternalEscalationCompilerTests
{
    [Fact]
    public void Compile_WhenOutcomeNotificationDoesNotExist_RejectsConfiguration()
    {
        var config = ValidConfig();
        config.Escalations.External.Events["order_created"].OutcomeEvents["accepted"] = "missing_event";

        var result = Compiler().Compile(config);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == "unknown_notification");
    }

    [Fact]
    public void Compile_WithDurableOutcomeRouteConfigured_AcceptsConfiguration()
    {
        var result = Compiler().Compile(ValidConfig());

        result.IsValid.Should().BeTrue(string.Join("; ", result.Diagnostics.Select(x => x.Message)));
    }

    private static AgentConfigurationCompiler Compiler() => new(new AgentOperationRegistry([]));

    private static AgentConfig ValidConfig()
    {
        var sequences = new MessageSequenceCatalog
        {
            ["delivery_request"] = new() { Messages = [new MessageSequenceStep { Body = "Pedido pendiente" }] },
            ["delivery_result"] = new() { Messages = [new MessageSequenceStep { Body = "Resultado recibido" }] }
        };
        return new AgentConfig
        {
            Flows = [new AgentFlowDefinition { Id = "delivery", Type = FlowTypes.Primary }],
            MessageSequences = sequences,
            Notifications = new NotificationDefinitions
            {
                ["delivery_confirmed"] = new()
                {
                    Enabled = true,
                    Deliveries =
                    [
                        new EventNotificationDeliveryConfig
                        {
                            Id = "customer",
                            Recipients = ["{customer_phone}"],
                            SendMessageSequence = "delivery_result"
                        }
                    ]
                }
            },
            Escalations = new EscalationDefinitions
            {
                External = new ExternalEscalationDefinitions
                {
                    Enabled = true,
                    Events = new Dictionary<string, ExternalEscalationEventDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["order_created"] = new()
                        {
                            Enabled = true,
                            ContactType = "domicilio",
                            SendMessageSequence = "delivery_request",
                            AttemptTimeoutMinutes = 15,
                            OutcomeEvents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["accepted"] = "delivery_confirmed"
                            }
                        }
                    }
                }
            }
        };
    }
}
