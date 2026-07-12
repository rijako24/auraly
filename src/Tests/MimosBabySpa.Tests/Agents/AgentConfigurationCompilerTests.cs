using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentConfigurationCompilerTests
{
    [Fact]
    public void Compile_WithRegisteredOperationOutcomeAndTemplate_Succeeds()
    {
        var compilation = Compiler().Compile(Config(
            outcomes: ["availability.options_available"],
            includeTemplate: true));

        compilation.IsValid.Should().BeTrue(string.Join("; ", compilation.Diagnostics.Select(value => value.Message)));
        compilation.Configuration!.Operations.Should().ContainKey("reservation.check_availability");
    }

    [Fact]
    public void Compile_WithUnknownOutcomeOrMissingTemplate_RejectsConfiguration()
    {
        var compilation = Compiler().Compile(Config(
            outcomes: ["availability.invented"],
            includeTemplate: false));

        compilation.IsValid.Should().BeFalse();
        compilation.Diagnostics.Should().Contain(value => value.Code == "unknown_outcome");
        compilation.Diagnostics.Should().Contain(value => value.Code == "missing_operation_template");
    }

    [Fact]
    public void Compile_WithEnforcedOperatingHoursAndNoConfiguredResponse_RejectsConfiguration()
    {
        var config = Config(["availability.options_available"], includeTemplate: true);
        config.OperatingHours.Enforce = true;

        var compilation = Compiler().Compile(config);

        compilation.IsValid.Should().BeFalse();
        compilation.Diagnostics.Should().Contain(value =>
            value.Path == "operatingHours.outsideHours" && value.Code == "response_required");
    }

    [Fact]
    public void Compile_WithNonPositiveFlowTtl_RejectsConfiguration()
    {
        var config = new AgentConfig
        {
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "booking",
                    Type = FlowTypes.Primary,
                    Stages = [new AgentFlowStage { Id = "booking" }]
                },
                new AgentFlowDefinition
                {
                    Id = "management",
                    Type = FlowTypes.Secondary,
                    TtlSeconds = 0,
                    Stages = [new AgentFlowStage { Id = "management" }]
                }
            ]
        };

        var compilation = Compiler().Compile(config);

        compilation.IsValid.Should().BeFalse();
        compilation.Diagnostics.Should().Contain(value =>
            value.Path == "flows[management].ttlSeconds"
            && value.Code == "invalid_flow_ttl");
    }
    private static AgentConfigurationCompiler Compiler() => new(
        new AgentOperationRegistry([new AvailabilityOperationStub()]));

    private static AgentConfig Config(IReadOnlyList<string> outcomes, bool includeTemplate)
    {
        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (includeTemplate)
            templates["availability_slots"] = "{{#each options}}{{this}}{{/each}}";

        return new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry
                {
                    Key = "service", Label = "servicio", Type = "string", Source = "user"
                },
                new FactSchemaEntry
                {
                    Key = "desired_date", Label = "fecha", Type = "date", Source = "user"
                },
                new FactSchemaEntry
                {
                    Key = "availability_checked", Label = "disponibilidad", Type = "boolean", Source = "system"
                }
            ],
            Templates = templates,
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "booking",
                    Type = FlowTypes.Primary,
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "availability",
                            AdvanceWhenFacts = ["availability_checked"],
                            Collect = ["desired_date"],
                            Actions =
                            [
                                new StageActionDefinition
                                {
                                    Id = "check_availability",
                                    Operation = "reservation.check_availability",
                                    Trigger = StageActionTriggers.WhenReady,
                                    Arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["service"] = Json("{{fact.service}}"),
                                        ["date"] = Json("{{fact.desired_date}}")
                                    },
                                    Condition = new StageConditionDefinition
                                    {
                                        All =
                                        [
                                            new StageConditionDefinition { FactPresent = "service" },
                                            new StageConditionDefinition { FactPresent = "desired_date" }
                                        ]
                                    },
                                    OnOutcome = outcomes.ToDictionary(
                                        value => value,
                                        _ => new StageOutcomeHandlerDefinition(),
                                        StringComparer.OrdinalIgnoreCase)
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private static JsonElement Json(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private sealed class AvailabilityOperationStub : IAgentOperation
    {
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok("availability.options_available", new { }));
    }
}
