using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Time;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class TemporalReferenceBuilderTests
{
    private readonly TemporalReferenceBuilder _builder = new();

    [Fact]
    public void Build_IncludesTodayTomorrowAndCalendar()
    {
        var tz = BusinessTimeZoneResolver.Resolve(BusinessClock.DefaultTimeZoneId);
        var today = new DateOnly(2026, 5, 21);
        var snapshot = new BusinessClockSnapshot(
            Guid.NewGuid(),
            new DateTimeOffset(today.ToDateTime(new TimeOnly(14, 0)), tz.GetUtcOffset(today.ToDateTime(new TimeOnly(14, 0)))),
            today,
            tz);

        var context = _builder.Build(snapshot, lookaheadDays: 7);
        var block = context.ToPromptBlock();

        block.Should().Contain("## CONTEXTO TEMPORAL");
        block.Should().Contain("hoy → 2026-05-21");
        block.Should().Contain("mañana → 2026-05-22");
        block.Should().Contain("pasado mañana → 2026-05-23");
        block.Should().Contain("2026-05-21 — jueves (hoy)");
        block.Should().Contain("2026-05-22 — viernes (mañana)");
        context.UpcomingDays.Should().HaveCount(7);
    }

    [Fact]
    public void Build_IncludesUpcomingWeekdaysInCalendar()
    {
        var tz = BusinessTimeZoneResolver.Resolve(BusinessClock.DefaultTimeZoneId);
        var today = new DateOnly(2026, 5, 21); // jueves
        var snapshot = new BusinessClockSnapshot(
            Guid.NewGuid(),
            new DateTimeOffset(today.ToDateTime(new TimeOnly(10, 0)), tz.GetUtcOffset(today.ToDateTime(new TimeOnly(10, 0)))),
            today,
            tz);

        var context = _builder.Build(snapshot, lookaheadDays: 14);
        context.UpcomingDays.Should().Contain(d => d.IsoDate == "2026-05-28" && d.WeekdayName == "jueves");
    }
}

public class AgentDateRulesTests
{
    [Fact]
    public void IsPastDate_UsesBusinessToday()
    {
        var today = new DateOnly(2026, 5, 21);
        AgentDateRules.IsPastDate(today.AddDays(-1), today).Should().BeTrue();
        AgentDateRules.IsPastDate(today, today).Should().BeFalse();
        AgentDateRules.IsPastDate(today.AddDays(1), today).Should().BeFalse();
    }
}
