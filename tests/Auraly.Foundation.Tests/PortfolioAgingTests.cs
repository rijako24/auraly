using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Foundation.Tests;

public sealed class PortfolioAgingTests
{
    private static readonly TimeZoneInfo Zone=TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    [Theory]
    [InlineData("2026-11-04T23:30:00-05:00", "2026-11-12T00:00:00-05:00", 8, 5, -3)]
    [InlineData("2026-11-04T03:00:00+00:00", "2026-11-09T03:00:00+00:00", 5, 6, 1)]
    [InlineData("2026-11-01T12:00:00-05:00", "2026-11-09T00:00:00-05:00", 8, 8, 0)]
    public void Document_terms_age_and_signed_maturity_use_calendar_days(string issued, string due,
        int termDays, int ageDays, int daysPastDue)
    {
        var result = PortfolioAging.Describe(DateTimeOffset.Parse(issued), DateTimeOffset.Parse(due), new(2026,11,9), Zone);
        Assert.Equal(termDays,result.TermDays);
        Assert.Equal(ageDays,result.AgeDays);
        Assert.Equal(daysPastDue,result.DaysPastDue);
    }

    [Theory]
    [InlineData(0,"NotDue")]
    [InlineData(1,"Overdue1To30")]
    [InlineData(30,"Overdue1To30")]
    [InlineData(31,"Overdue31To60")]
    [InlineData(60,"Overdue31To60")]
    [InlineData(61,"Overdue61To90")]
    [InlineData(90,"Overdue61To90")]
    [InlineData(91,"OverdueOver90")]
    public void Bands_use_the_business_calendar(int days,string band)
    {
        var reference=new DateOnly(2026,11,9);
        var due=new DateTimeOffset(reference.AddDays(-days).ToDateTime(new TimeOnly(12,0)),TimeSpan.FromHours(-5));
        var age=PortfolioAging.Calculate(100,due,reference,Zone);
        Assert.Equal(days,age.OverdueDays);Assert.Equal(band,age.BandCode);
    }

    [Fact]
    public void An_utc_date_is_not_mistaken_for_the_business_due_day()
    {
        var age=PortfolioAging.Calculate(100,new(2026,11,9,3,0,0,TimeSpan.Zero),new(2026,11,9),Zone);
        Assert.Equal(1,age.OverdueDays);
        Assert.Equal("Overdue1To30",age.BandCode);
        Assert.Equal(new DateOnly(2026,11,8),age.DueOn);
    }

    [Fact]
    public void Paid_documents_have_no_overdue_balance()
    {
        var age=PortfolioAging.Calculate(0,new(2025,1,1,12,0,0,TimeSpan.Zero),new(2026,11,9),Zone);
        Assert.Equal(0,age.OverdueDays);Assert.Equal("NotDue",age.BandCode);
        Assert.Equal(new DateOnly(2025,1,1),age.DueOn);
    }
}
