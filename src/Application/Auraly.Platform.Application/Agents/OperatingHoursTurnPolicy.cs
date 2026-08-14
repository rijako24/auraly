using System.Globalization;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Application.Time;

namespace Auraly.Platform.Application.Agents;

public interface IOperatingHoursTurnPolicy
{
    Task<OperatingHoursTurnContext> EvaluateAsync(
        AgentConfig config,
        BusinessClockSnapshot clockSnapshot,
        CancellationToken ct = default);
}

public sealed record OperatingHoursTurnContext(
    bool IsEnforced,
    bool IsOutsideOperatingHours,
    string? NextOperatingWindowText)
{
    public static OperatingHoursTurnContext Disabled { get; } = new(false, false, null);
}

public sealed class OperatingHoursTurnPolicy : IOperatingHoursTurnPolicy
{
    private const int LookaheadDays = 14;
    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly IWorkingHoursService _workingHours;

    public OperatingHoursTurnPolicy(IWorkingHoursService workingHours)
    {
        _workingHours = workingHours;
    }

    public async Task<OperatingHoursTurnContext> EvaluateAsync(
        AgentConfig config,
        BusinessClockSnapshot clockSnapshot,
        CancellationToken ct = default)
    {
        if (!config.OperatingHours.Enforce)
            return OperatingHoursTurnContext.Disabled;

        var status = await ResolveStatusAsync(config.BusinessId, clockSnapshot, ct);
        return new OperatingHoursTurnContext(
            true,
            !status.IsOpen,
            status.NextWindowText);
    }

    private async Task<OperatingHoursStatus> ResolveStatusAsync(
        Guid businessId,
        BusinessClockSnapshot clockSnapshot,
        CancellationToken ct)
    {
        var nowTime = clockSnapshot.Now.TimeOfDay;
        OperatingWindow? nextWindow = null;

        for (var offset = 0; offset <= LookaheadDays; offset++)
        {
            var date = clockSnapshot.Today.AddDays(offset);
            var blocks = await _workingHours.GetEffectiveBusinessWorkingHoursAsync(businessId, date, ct);
            foreach (var block in blocks.Where(b => b.IsValid()).OrderBy(b => b.OpenTime))
            {
                var window = new OperatingWindow(date, block.OpenTime, block.CloseTime);

                if (offset == 0 && nowTime >= window.OpenTime && nowTime < window.CloseTime)
                    return new OperatingHoursStatus(true, FormatWindow(window, clockSnapshot.Today));

                if (offset == 0 && window.CloseTime <= nowTime)
                    continue;

                nextWindow ??= window;
            }
        }

        return new OperatingHoursStatus(false, nextWindow is null ? null : FormatWindow(nextWindow, clockSnapshot.Today));
    }

    private static string FormatWindow(OperatingWindow window, DateOnly referenceDate)
    {
        if (window.Date == referenceDate)
            return $"hoy de {FormatTime(window.OpenTime)} a {FormatTime(window.CloseTime)}";

        var day = window.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd d 'de' MMMM", SpanishCulture);
        return $"{day} de {FormatTime(window.OpenTime)} a {FormatTime(window.CloseTime)}";
    }

    private static string FormatTime(TimeSpan time) =>
        DateTime.Today.Add(time).ToString("h:mm tt", SpanishCulture).ToLowerInvariant().Replace('\u00A0', ' ');

    private sealed record OperatingHoursStatus(bool IsOpen, string? NextWindowText);

    private sealed record OperatingWindow(DateOnly Date, TimeSpan OpenTime, TimeSpan CloseTime);
}
