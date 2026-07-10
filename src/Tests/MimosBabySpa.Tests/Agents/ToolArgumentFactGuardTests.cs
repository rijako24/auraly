using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ToolArgumentFactGuardTests
{
    [Fact]
    public void BuildUnsupportedUserFactResult_BlocksToolArgumentWhenUserDidNotProvidePendingFact()
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
            ctx);

        result.Should().Contain("tool_argument_requires_user_fact_capture");
        result.Should().Contain("desired_date");
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
            ctx);

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
            ctx);

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
            ctx);

        result.Should().Contain("tool_argument_requires_user_fact_capture");
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
            ctx);

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
            ctx);

        result.Should().BeNull();
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
}





