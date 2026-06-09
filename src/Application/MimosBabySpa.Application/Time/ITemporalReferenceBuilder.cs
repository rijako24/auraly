namespace MimosBabySpa.Application.Time;

public interface ITemporalReferenceBuilder
{
    TemporalReferenceContext Build(BusinessClockSnapshot snapshot, int lookaheadDays = 14);
}
