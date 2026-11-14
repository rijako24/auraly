using Auraly.BuildingBlocks.Domain.Time;

namespace Auraly.Foundation.Tests;

public sealed class BusinessCalendarTests
{
    [Theory]
    [InlineData("America/Bogota", "2026-11-12", "2026-11-12T05:00:00Z")]
    [InlineData("Asia/Tokyo", "2026-11-12", "2026-11-11T15:00:00Z")]
    [InlineData("Pacific/Kiritimati", "2026-11-12", "2026-11-11T10:00:00Z")]
    [InlineData("America/New_York", "2026-03-08", "2026-03-08T05:00:00Z")]
    [InlineData("America/New_York", "2026-03-09", "2026-03-09T04:00:00Z")]
    [InlineData("America/New_York", "2026-11-01", "2026-11-01T04:00:00Z")]
    [InlineData("America/New_York", "2026-11-02", "2026-11-02T05:00:00Z")]
    [InlineData("America/Sao_Paulo", "2018-11-04", "2018-11-04T03:00:00Z")]
    [InlineData("Pacific/Apia", "2011-12-30", "2011-12-30T10:00:00Z")]
    [InlineData("Pacific/Apia", "2011-12-31", "2011-12-30T10:00:00Z")]
    public void Boundary_is_the_first_instant_of_the_requested_business_day(string zoneId, string date, string expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var day = DateOnly.Parse(date);
        var actual = BusinessCalendar.StartOfDay(day, zone);
        Assert.Equal(DateTimeOffset.Parse(expected), actual);
        Assert.True(BusinessCalendar.DateAt(actual.AddTicks(-1), zone) < day);
        Assert.True(BusinessCalendar.DateAt(actual, zone) >= day);
    }

    [Fact]
    public void A_skipped_day_can_be_filtered_but_cannot_be_the_effective_date_of_a_payment()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Apia");
        var skipped = new DateOnly(2011, 12, 30);
        Assert.Equal(BusinessCalendar.StartOfDay(skipped, zone), BusinessCalendar.StartOfDay(skipped.AddDays(1), zone));
        Assert.Throws<ArgumentException>(() => BusinessCalendar.AtNoon(skipped, zone));
        Assert.Equal(DateTimeOffset.Parse("2011-12-30T22:00:00Z"), BusinessCalendar.AtNoon(skipped.AddDays(1), zone));
    }
}
