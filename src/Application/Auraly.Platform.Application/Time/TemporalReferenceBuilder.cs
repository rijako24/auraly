namespace Auraly.Platform.Application.Time;

public sealed class TemporalReferenceBuilder : ITemporalReferenceBuilder
{
    public TemporalReferenceContext Build(BusinessClockSnapshot snapshot, int lookaheadDays = 14)
    {
        if (lookaheadDays < 1)
            lookaheadDays = 1;

        var days = new List<TemporalReferenceContext.TemporalDayEntry>(lookaheadDays);
        for (var i = 0; i < lookaheadDays; i++)
        {
            var date = snapshot.Today.AddDays(i);
            var weekday = TemporalReferenceContext.FormatWeekday(date.DayOfWeek);
            var relative = TemporalReferenceContext.FormatRelativeLabel(date, snapshot.Today);
            days.Add(new TemporalReferenceContext.TemporalDayEntry(
                date.ToString("yyyy-MM-dd"),
                weekday,
                relative));
        }

        return new TemporalReferenceContext
        {
            TimeZoneId = snapshot.TimeZone.Id,
            Now = snapshot.Now,
            Today = snapshot.Today,
            UpcomingDays = days
        };
    }
}
