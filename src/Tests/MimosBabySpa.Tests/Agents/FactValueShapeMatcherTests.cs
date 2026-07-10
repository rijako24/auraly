using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FactValueShapeMatcherTests
{
    [Fact]
    public void MessageMatchesFactShape_UsesFactType_NotRoleName()
    {
        var schema = new[]
        {
            new FactSchemaEntry { Key = "contact_email", Type = "email", Role = "booking.time" }
        };

        FactValueShapeMatcher.MessageMatchesFactShape(schema, "contact_email", "a las 2")
            .Should().BeFalse();
    }

    [Fact]
    public void MessageMatchesFactShape_TimeTypeAcceptsColloquialHour()
    {
        var schema = new[]
        {
            new FactSchemaEntry { Key = "slot", Type = "time", Role = "custom.slot" }
        };

        FactValueShapeMatcher.MessageMatchesFactShape(schema, "slot", "a las 2")
            .Should().BeTrue();
    }

    [Fact]
    public void MessageMatchesFactShape_TimeTypeAcceptsHourAndMinutesInsideSentence()
    {
        var schema = new[]
        {
            new FactSchemaEntry { Key = "slot", Type = "time", Role = "custom.slot" }
        };

        FactValueShapeMatcher.MessageMatchesFactShape(schema, "slot", "A las 8:30")
            .Should().BeTrue();
    }
    [Fact]
    public void MessageMatchesFactShape_DateTypeAcceptsConfiguredAlias()
    {
        var schema = new[]
        {
            new FactSchemaEntry { Key = "desired_date", Type = "date", Aliases = ["hoy"] }
        };

        FactValueShapeMatcher.MessageMatchesFactShape(schema, "desired_date", "para hoy")
            .Should().BeTrue();
    }
}



