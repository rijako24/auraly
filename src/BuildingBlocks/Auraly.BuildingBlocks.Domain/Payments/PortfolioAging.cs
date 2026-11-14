using Auraly.BuildingBlocks.Domain.Time;

namespace Auraly.BuildingBlocks.Domain.Payments;

public readonly record struct PortfolioAge(int OverdueDays, string BandCode)
{
    public DateOnly DueOn { get; init; }
}

public readonly record struct PortfolioDocumentAge(int TermDays, int AgeDays, int DaysPastDue);

public static class PortfolioAging
{
    public const int FirstBandDays=30;
    public const int SecondBandDays=60;
    public const int ThirdBandDays=90;

    public static PortfolioDocumentAge Describe(DateTimeOffset issuedAt, DateTimeOffset dueAt,
        DateOnly referenceDate, TimeZoneInfo zone)
    {
        var issuedOn = BusinessCalendar.DateAt(issuedAt, zone);
        var dueOn = BusinessCalendar.DateAt(dueAt, zone);
        return new(dueOn.DayNumber - issuedOn.DayNumber, referenceDate.DayNumber - issuedOn.DayNumber,
            referenceDate.DayNumber - dueOn.DayNumber);
    }

    public static PortfolioAge Calculate(decimal outstanding, DateTimeOffset dueAt,
        DateOnly referenceDate, TimeZoneInfo zone)
    {
        var dueOn=BusinessCalendar.DateAt(dueAt,zone);
        var days=outstanding>0 ? Math.Max(0,referenceDate.DayNumber-dueOn.DayNumber) : 0;
        return new(days,days switch
        {
            0=>"NotDue",<=FirstBandDays=>"Overdue1To30",<=SecondBandDays=>"Overdue31To60",
            <=ThirdBandDays=>"Overdue61To90",_=>"OverdueOver90"
        }) { DueOn=dueOn };
    }
}
