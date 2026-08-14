namespace Auraly.Platform.Application.Time;

public interface ITemporalReferenceBuilder
{
    TemporalReferenceContext Build(BusinessClockSnapshot snapshot, int lookaheadDays = 14);
}
