using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools;
using System.Text.Json;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class SetFactParametersSchemaBuilderTests
{
    [Fact]
    public void Build_WithUserFacts_EmitsKeyEnumFromSchema()
    {
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "desired_date", Label = "fecha", Type = "date", Source = "user" },
                new FactSchemaEntry { Key = "desired_time", Label = "hora", Type = "time", Source = "user" },
                new FactSchemaEntry { Key = "service", Label = "servicio", Type = "string", Source = "user" },
                new FactSchemaEntry { Key = "session.engagement", Label = "engagement", Source = "session" }
            ]
        };

        using var doc = JsonDocument.Parse(SetFactParametersSchemaBuilder.Build(config));
        var keyProp = doc.RootElement.GetProperty("properties").GetProperty("key");
        var enumValues = keyProp.GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        enumValues.Should().BeEquivalentTo(["desired_date", "desired_time", "service"]);
        enumValues.Should().NotContain("session.engagement");
        enumValues.Should().NotContain("date");
        enumValues.Should().NotContain("time");
    }

    [Fact]
    public void Build_DescriptionsDoNotIncludeNoneValueHints()
    {
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry
                {
                    Key = "add_ons",
                    Label = "complementos",
                    Type = "string",
                    Source = "user"
                }
            ]
        };

        var json = SetFactParametersSchemaBuilder.Build(config);
        json.Should().NotContain("ninguno");
        json.Should().Contain("complementos");
    }

    [Fact]
    public void Build_EmptyUserFacts_FallsBackToStaticSchema()
    {
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "session.engagement", Source = "session" }
            ]
        };

        SetFactParametersSchemaBuilder.Build(config)
            .Should().Be(SetFactParametersSchemaBuilder.FallbackSchema);
    }
}

public sealed class ToolExecutionOutcomeTests
{
    [Fact]
    public void Parse_RecoverableErrorCode_IsMarkedRecoverable()
    {
        var json = """{"ok":false,"error":{"code":"unknown_fact_key","message":"x","hint":"y","recoverable":true}}""";
        var outcome = ToolExecutionOutcome.Parse(json);

        outcome.IsError.Should().BeTrue();
        outcome.ErrorCode.Should().Be("unknown_fact_key");
        outcome.IsRecoverableError.Should().BeTrue();
    }

    [Fact]
    public void Parse_FatalErrorCode_IsNotRecoverable()
    {
        var json = """{"ok":false,"error":{"code":"tool_exception","message":"x"}}""";
        var outcome = ToolExecutionOutcome.Parse(json);

        outcome.IsRecoverableError.Should().BeFalse();
    }
}
