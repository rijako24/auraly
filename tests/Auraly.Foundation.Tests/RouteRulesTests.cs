using Auraly.Domain.Routes;

namespace Auraly.Foundation.Tests;

public sealed class RouteRulesTests
{
    [Fact]
    public void Code_is_normalized_without_accepting_ambiguous_characters()
    {
        Assert.Equal("RUTA-NORTE_1", RouteRules.NormalizeCode(" ruta-norte_1 "));
        Assert.Throws<ArgumentException>(() => RouteRules.NormalizeCode("ruta norte"));
    }

    [Fact]
    public void Schedule_requires_iso_unique_days_and_positive_order()
    {
        var values = RouteRules.Schedules([
            (5, 2, (TimeOnly?)new TimeOnly(9, 0)),
            (1, 1, (TimeOnly?)null)]);
        Assert.Equal([1, 5], values.Select(value => value.DayOfWeek));
        Assert.Throws<ArgumentException>(() => RouteRules.Schedules(Array.Empty<(int, int, TimeOnly?)>()));
        Assert.Throws<ArgumentException>(() => RouteRules.Schedules([(1, 1, null), (1, 2, null)]));
        Assert.Throws<ArgumentException>(() => RouteRules.Schedules([(8, 1, null)]));
        Assert.Throws<ArgumentException>(() => RouteRules.Schedules([(1, 0, null)]));
    }

    [Fact]
    public void Reorder_requires_the_complete_same_stop_collection()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.Equal([second, first], RouteRules.CompleteOrder([first, second], [second, first]));
        Assert.Throws<ArgumentException>(() => RouteRules.CompleteOrder([first, second], [first]));
        Assert.Throws<ArgumentException>(() => RouteRules.CompleteOrder([first, second], [first, first]));
    }

    [Theory]
    [InlineData(0, 0, false, "Draft")]
    [InlineData(1, 0, false, "Draft")]
    [InlineData(1, 1, false, "Ready")]
    [InlineData(1, 1, true, "AttentionRequired")]
    public void Preparation_is_derived_not_manually_stored(
        int schedules, int stops, bool conflict, string expected)
    {
        Assert.Equal(expected, RouteRules.PreparationStatus(schedules, stops, conflict));
    }
}
