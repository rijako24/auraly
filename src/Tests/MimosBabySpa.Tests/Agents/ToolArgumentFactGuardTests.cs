using FluentAssertions;
using MimosBabySpa.Application.Agents;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Domain.Entities;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ToolArgumentFactGuardTests
{
    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsAvailabilityLookupEvenWhenDateWasNotInLatestMessage()
    {
        var config = BuildConfig();
        var stage = BuildSchedulingStage();
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "sin ellos",
            Facts = { ["service"] = "Corte basico" }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "check_availability",
            """{"service":"Corte basico","date":"2026-07-09"}""",
            ctx,
            [new TestTool("check_availability", [ToolCapabilities.AvailabilityCheck])]);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsToolArgumentWhenFactAlreadyExists()
    {
        var config = BuildConfig();
        var stage = BuildSchedulingStage();
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "sin ellos",
            Facts =
            {
                ["service"] = "Corte basico",
                ["desired_date"] = "2026-07-09"
            }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "check_availability",
            """{"service":"Corte basico","date":"2026-07-09"}""",
            ctx,
            [new TestTool("check_availability", [ToolCapabilities.AvailabilityCheck])]);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsToolArgumentWhenLatestMessageMatchesDateAlias()
    {
        var config = BuildConfig();
        var stage = BuildSchedulingStage();
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "para hoy",
            Facts = { ["service"] = "Corte basico" }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "check_availability",
            """{"service":"Corte basico","date":"2026-07-09"}""",
            ctx,
            [new TestTool("check_availability", [ToolCapabilities.AvailabilityCheck])]);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildUnsupportedUserFactResult_BlocksSetFactWhenUserDidNotProvidePendingFact()
    {
        var config = BuildConfig();
        var stage = BuildSchedulingStage();
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "sin ellos",
            Facts = { ["service"] = "Corte basico" }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"desired_date","value":"2026-07-09"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().Contain("tool_argument_requires_user_fact_capture");
        result.Should().Contain("recover_fact_capture_before_continuing");
        result.Should().Contain("fact_was_saved");
        result.Should().Contain("false");
        result.Should().Contain("desired_date");
    }


    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsAddOnsFactSoCatalogValidationCanDecide()
    {
        var config = BuildConfig();
        var stage = BuildAddOnsStage();
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "cambia el servicio a corte premium",
            Facts = { ["service"] = "Corte premium" }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"add_ons","value":"ninguno"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsStringFactWhenConfiguredAliasIsMentioned()
    {
        var config = BuildConfig();
        var stage = BuildAddOnsStage();
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "sin adicionales",
            Facts = { ["service"] = "Corte premium" }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"add_ons","value":"ninguno"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().BeNull();
    }
    [Theory]
    [InlineData("order_finalized", "order.finalized", "solo eso", "solo eso")]
    [InlineData("cart_review_confirmed", "order.cart_review_confirmed", "correcto", "esta correcto")]
    public void BuildUnsupportedUserFactResult_AllowsBooleanCheckpointWhenConfiguredAliasIsMentioned(
        string key,
        string role,
        string alias,
        string message)
    {
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry
                {
                    Key = key,
                    Role = role,
                    Type = "boolean",
                    Source = "user",
                    Aliases = [alias]
                }
            ]
        };
        var stage = new AgentFlowStage { Id = "checkpoint", Collect = [key] };
        var ctx = new AgentToolContext { LatestUserMessage = message };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            $$$"""{"key":"{{key}}","value":true}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().BeNull();
    }


    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsStringFactWhenLatestMessageFuzzyMatchesConfiguredAlias()
    {
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry
                {
                    Key = "customer_type",
                    Type = "string",
                    Source = "user",
                    Aliases = ["restaurante"]
                }
            ]
        };
        var stage = new AgentFlowStage { Id = "customer_profile", Collect = ["customer_type"] };
        var ctx = new AgentToolContext { LatestUserMessage = "restaurate" };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"customer_type","value":"Restaurante"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().BeNull();
    }
    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsStringFactWhenLatestMessageSelectsPreviousAssistantOption()
    {
        var config = BuildCustomerTypeConfig();
        var stage = new AgentFlowStage { Id = "customer_type", Collect = ["customer_type"] };
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "la A",
            Conversation = new Conversation
            {
                Messages =
                {
                    new Message
                    {
                        Sender = "Bot",
                        MessageText = "Cual de estas opciones describe mejor tu perfil? A. Hogar B. Tienda o minimercado C. Restaurante",
                        Timestamp = DateTime.UtcNow.AddSeconds(-10)
                    }
                }
            }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"customer_type","value":"Hogar"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildUnsupportedUserFactResult_BlocksStringFactWhenSelectedAssistantOptionDoesNotMatchValue()
    {
        var config = BuildCustomerTypeConfig();
        var stage = new AgentFlowStage { Id = "customer_type", Collect = ["customer_type"] };
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "la B",
            Conversation = new Conversation
            {
                Messages =
                {
                    new Message
                    {
                        Sender = "Bot",
                        MessageText = "Cual de estas opciones describe mejor tu perfil? A. Hogar B. Tienda o minimercado C. Restaurante",
                        Timestamp = DateTime.UtcNow.AddSeconds(-10)
                    }
                }
            }
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"customer_type","value":"Restaurante"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().Contain("tool_argument_requires_user_fact_capture");
    }
    [Fact]
    public void BuildUnsupportedUserFactResult_DoesNotBlockSecondaryFlowDomainTools()
    {
        var config = BuildConfigWithFlows();
        var stage = new AgentFlowStage
        {
            Id = "reservation_management",
            Collect = ["desired_date", "desired_time"]
        };
        var ctx = new AgentToolContext
        {
            LatestUserMessage = "a las 8:30"
        };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "manage_reservation",
            """{"action":"request_reschedule","date":"2038-10-16","time":"08:30"}""",
            ctx);

        result.Should().BeNull();
    }

    private static AgentConfig BuildCustomerTypeConfig() => new()
    {
        FactSchema =
        [
            new FactSchemaEntry
            {
                Key = "customer_type",
                Type = "string",
                Source = "user",
                Aliases = ["hogar", "tienda", "minimercado", "restaurante", "comida rapida", "distribuidor"]
            }
        ]
    };
    private static AgentConfig BuildConfig() => new()
    {
        FactSchema =
        [
            new FactSchemaEntry
            {
                Key = "service",
                Role = "booking.service",
                Type = "string",
                Source = "user",
                ValueSource = "catalog"
            },
            new FactSchemaEntry
            {
                Key = "baby_name",
                Role = "baby.name",
                Type = "string",
                Source = "user"
            },
            new FactSchemaEntry
            {
                Key = "desired_date",
                Role = "booking.date",
                Type = "date",
                Source = "user",
                Aliases = ["fecha", "dia", "hoy", "manana"]
            },
            new FactSchemaEntry
            {
                Key = "desired_time",
                Role = "booking.time",
                Type = "time",
                Source = "user",
                Aliases = ["hora", "horario"]
            },
            new FactSchemaEntry
            {
                Key = "add_ons",
                Role = "booking.addons",
                Type = "string",
                Source = "user",
                ValueSource = "catalog",
                Aliases = ["adicional", "adicionales"]
            },
            new FactSchemaEntry
            {
                Key = "availability_checked",
                Role = "booking.availability_checked",
                Type = "string",
                Source = "system"
            }
        ]
    };


    private static AgentConfig BuildConfigWithFlows()
    {
        var config = BuildConfig();
        return new AgentConfig
        {
            FactSchema = config.FactSchema,
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "booking",
                    Type = "primary",
                    Stages = [BuildSchedulingStage(), BuildAddOnsStage()]
                },
                new AgentFlowDefinition
                {
                    Id = "reservation_management",
                    Type = "secondary",
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "reservation_management",
                            Collect = ["desired_date", "desired_time"]
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void BuildUnsupportedUserFactResult_BlocksInventedStringFactWithoutLatestMessageSupport()
    {
        var config = BuildConfig();
        var stage = new AgentFlowStage { Id = "discovery", Collect = ["baby_name"] };
        var ctx = new AgentToolContext { LatestUserMessage = "hola" };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"baby_name","value":"unknown"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().Contain("tool_argument_requires_user_fact_capture");
        result.Should().Contain("baby_name");
    }

    [Fact]
    public void BuildUnsupportedUserFactResult_AllowsStringFactWhenValueIsInLatestMessage()
    {
        var config = BuildConfig();
        var stage = new AgentFlowStage { Id = "discovery", Collect = ["baby_name"] };
        var ctx = new AgentToolContext { LatestUserMessage = "Mi bebe se llama Mia y tiene 8 meses" };

        var result = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
            config,
            stage,
            "set_fact",
            """{"key":"baby_name","value":"Mia"}""",
            ctx,
            [new TestTool("set_fact", [ToolCapabilities.FactWrite])]);

        result.Should().BeNull();
    }

    private static AgentFlowStage BuildSchedulingStage() => new()
    {
        Id = "scheduling",
        Collect = ["desired_date", "desired_time"],
        AdvanceWhenFacts = ["availability_checked"]
    };

    private static AgentFlowStage BuildAddOnsStage() => new()
    {
        Id = "add_ons",
        Collect = ["add_ons"],
        AdvanceWhenFacts = ["add_ons"]
    };
    private sealed class TestTool(string name, IReadOnlyList<string>? capabilities = null) : IAgentTool
    {
        public string Name { get; } = name;
        public IReadOnlyList<string> Capabilities { get; } = capabilities ?? [];
        public string Description => string.Empty;
        public string ParametersSchema => "{}";
        public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default) =>
            Task.FromResult("{}");
    }
}


