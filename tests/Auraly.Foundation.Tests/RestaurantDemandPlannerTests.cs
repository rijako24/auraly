using Auraly.BuildingBlocks.Domain.RestaurantDemand;

namespace Auraly.Foundation.Tests;

public sealed class RestaurantDemandPlannerTests
{
    private static readonly DateOnly Friday = new(2026, 10, 9);

    [Fact]
    public void Comparable_days_exclude_closed_days_and_history_outside_eight_weeks()
    {
        Assert.Equal(1, RestaurantDemandPlanner.ComparableDayCount(Friday, 1,
            [Friday.AddDays(-7), Friday.AddDays(-63), Friday], Friday));
    }

    [Theory]
    [InlineData(1, 10, 30, 10)]
    [InlineData(2, 10, 30, 40)]
    [InlineData(3, 10, 30, 40)]
    [InlineData(4, 10, 30, 40)]
    [InlineData(7, 10, 30, 40)]
    [InlineData(8, 10, 30, 50)]
    [InlineData(9, 10, 30, 80)]
    [InlineData(1, 0, 30, 0)]
    [InlineData(2, 0, 30, 30)]
    [InlineData(2, 10, 0, 10)]
    [InlineData(8, 0, 0, 0)]
    [InlineData(9, 5, 25, 60)]
    public void Forecast_compares_each_future_weekday_with_its_own_history(
        int days, int fridayQuantity, int saturdayQuantity, int expected)
    {
        var product = Guid.NewGuid();
        var open = new[] { Friday.AddDays(-7), Friday.AddDays(-6) };
        var result = RestaurantDemandPlanner.Forecast(Friday, days, open,
            [new(product, open[0], fridayQuantity), new(product, open[1], saturdayQuantity)],
            [], []);
        Assert.Equal(expected, result[product].GrossQuantity);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 5, 10)]
    [InlineData(2, 0, 0, 0, 4, 8)]
    [InlineData(0, 1, 0, 0, 4, 8)]
    [InlineData(0, 0, 3, 0, 5, 7)]
    [InlineData(0, 0, 0, 4, 5, 6)]
    [InlineData(2, 1, 3, 1, 3, 2)]
    [InlineData(10, 0, 0, 0, 0, 0)]
    [InlineData(0, 5, 0, 0, 0, 0)]
    public void Forecast_nets_existing_preparations_before_ordering_raw_ingredients(
        int mealStock, int sauceStock, int rawStock, int incomingRaw,
        int expectedSauceProduction, int expectedRawPurchase)
    {
        var meal = Guid.NewGuid();
        var sauce = Guid.NewGuid();
        var raw = Guid.NewGuid();
        var pastFriday = Friday.AddDays(-7);
        var result = RestaurantDemandPlanner.Forecast(Friday, 1, [pastFriday],
            [new(meal, pastFriday, 10)],
            [new(meal, sauce, .5m), new(sauce, raw, 2m)],
            [new(meal, true, mealStock), new(sauce, true, sauceStock),
                new(raw, true, rawStock, incomingRaw)]);
        Assert.Equal(expectedSauceProduction, result[sauce].UncoveredQuantity);
        Assert.Equal(expectedRawPurchase, result[raw].UncoveredQuantity);
    }

    [Fact]
    public void Closed_weekday_is_not_treated_as_zero_demand()
    {
        var product = Guid.NewGuid();
        var twoWeeksAgo = Friday.AddDays(-14);
        var lastWeek = Friday.AddDays(-7);
        var result = RestaurantDemandPlanner.Forecast(Friday, 1, [twoWeeksAgo],
            [new(product, twoWeeksAgo, 12), new(product, lastWeek, 0)], [], []);
        Assert.Equal(12, result[product].GrossQuantity);
    }

    [Fact]
    public void Open_day_without_product_sales_counts_as_zero()
    {
        var product = Guid.NewGuid();
        var open = new[] { Friday.AddDays(-14), Friday.AddDays(-7) };
        var result = RestaurantDemandPlanner.Forecast(Friday, 1, open,
            [new(product, open[0], 12)], [], []);
        Assert.Equal(6, result[product].GrossQuantity);
    }

    [Fact]
    public void Future_plan_uses_recent_actual_history_instead_of_an_empty_future_window()
    {
        var product = Guid.NewGuid();
        var past = Friday.AddDays(-7);
        var result = RestaurantDemandPlanner.Forecast(Friday.AddDays(84), 1,
            [past], [new(product, past, 15)], [], [], Friday);
        Assert.Equal(15, result[product].GrossQuantity);
    }

    [Fact]
    public void Direct_recipe_without_finished_stock_consumes_components()
    {
        var meal = Guid.NewGuid();
        var raw = Guid.NewGuid();
        var past = Friday.AddDays(-7);
        var result = RestaurantDemandPlanner.Forecast(Friday, 1, [past],
            [new(meal, past, 7)], [new(meal, raw, 3)],
            [new(meal, false, 100), new(raw, true, 0)]);
        Assert.Equal(21, result[raw].UncoveredQuantity);
    }

    [Fact]
    public void Existing_cycle_is_rejected_instead_of_looping()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => RestaurantDemandPlanner.Forecast(
            Friday, 1, [], [], [new(first, second, 1), new(second, first, 1)], []));
    }

    [Fact]
    public void Negative_stock_is_not_available_to_cover_demand()
    {
        var product = Guid.NewGuid();
        var past = Friday.AddDays(-7);
        var result = RestaurantDemandPlanner.Forecast(Friday, 1, [past],
            [new(product, past, 4)], [], [new(product, true, -2)]);
        Assert.Equal(4, result[product].UncoveredQuantity);
    }
}
