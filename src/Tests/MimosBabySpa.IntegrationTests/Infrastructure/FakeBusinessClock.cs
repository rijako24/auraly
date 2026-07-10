using MimosBabySpa.Application.Time;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Reloj fijo para tests: mantiene las fechas absolutas de escenarios en el futuro.
/// </summary>
public sealed class FakeBusinessClock : IBusinessClock
{
    private readonly DateOnly _today;
    private readonly TimeOnly _time;
    private readonly TimeZoneInfo _timeZone;

    public FakeBusinessClock(
        DateOnly? today = null,
        TimeOnly? time = null,
        string timeZoneId = BusinessClock.DefaultTimeZoneId)
    {
        _today = today ?? new DateOnly(2026, 7, 8);
        _time = time ?? new TimeOnly(10, 0);
        _timeZone = BusinessTimeZoneResolver.Resolve(timeZoneId);
    }

    public Task<BusinessClockSnapshot> GetSnapshotAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var local = _today.ToDateTime(_time);
        var offset = _timeZone.GetUtcOffset(local);
        var now = new DateTimeOffset(local, offset);
        return Task.FromResult(new BusinessClockSnapshot(businessId, now, _today, _timeZone));
    }
}
