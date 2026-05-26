using FluentAssertions;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FactShapeValidatorTests
{
    [Fact]
    public void Validate_date_accepts_yyyy_MM_dd()
    {
        var entry = new FactSchemaEntry { Key = "desired_date", Type = "date" };
        FactShapeValidator.Validate(entry, "2026-05-27").Ok.Should().BeTrue();
    }

    [Fact]
    public void Validate_date_rejects_natural_language()
    {
        var entry = new FactSchemaEntry { Key = "desired_date", Type = "date" };
        FactShapeValidator.Validate(entry, "mañana").Ok.Should().BeFalse();
    }

    [Fact]
    public void Validate_number_respects_range()
    {
        var entry = new FactSchemaEntry
        {
            Key = "baby_age_months",
            Type = "number",
            Range = new FactNumericRange { Min = 0, Max = 60 }
        };

        FactShapeValidator.Validate(entry, "5").Ok.Should().BeTrue();
        FactShapeValidator.Validate(entry, "99").Ok.Should().BeFalse();
    }
}
