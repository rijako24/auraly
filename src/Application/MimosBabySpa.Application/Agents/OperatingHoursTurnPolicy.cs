using System.Globalization;
using MimosBabySpa.Application.Agents.Tools;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Time;

namespace MimosBabySpa.Application.Agents;

public interface IOperatingHoursTurnPolicy
{
    Task<OperatingHoursTurnPolicyResult> EvaluateAsync(
        AgentConfig config,
        BusinessClockSnapshot clockSnapshot,
        IReadOnlyList<IAgentTool> configuredTools,
        CancellationToken ct = default);
}

public sealed record OperatingHoursTurnPolicyResult(
    IReadOnlyList<IAgentTool> EffectiveTools,
    OperatingHoursTurnContext Context);

public sealed record OperatingHoursTurnContext(
    bool IsEnabled,
    bool IsOutsideOperatingHours,
    IReadOnlyList<string> GatedGroups,
    IReadOnlyList<string> BlockedToolNames,
    string? NextOperatingWindowText)
{
    public static OperatingHoursTurnContext Disabled { get; } = new(false, false, [], [], null);
}

public sealed class OperatingHoursTurnPolicy : IOperatingHoursTurnPolicy
{
    private const int LookaheadDays = 14;
    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly IWorkingHoursService _workingHours;
    private readonly ILogger<OperatingHoursTurnPolicy> _logger;

    public OperatingHoursTurnPolicy(IWorkingHoursService workingHours, ILogger<OperatingHoursTurnPolicy> logger)
    {
        _workingHours = workingHours;
        _logger = logger;
    }

    public async Task<OperatingHoursTurnPolicyResult> EvaluateAsync(
        AgentConfig config,
        BusinessClockSnapshot clockSnapshot,
        IReadOnlyList<IAgentTool> configuredTools,
        CancellationToken ct = default)
    {
        if (!config.OperatingHours.Enabled || config.OperatingHours.GatedGroups.Count == 0)
        {
            return new OperatingHoursTurnPolicyResult(configuredTools, OperatingHoursTurnContext.Disabled);
        }

        var gatedGroups = config.OperatingHours.GatedGroups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (gatedGroups.Count == 0)
            return new OperatingHoursTurnPolicyResult(configuredTools, OperatingHoursTurnContext.Disabled);

        var configuredGroups = configuredTools
            .SelectMany(tool => tool.OperatingGroups)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var gatedGroup in gatedGroups)
        {
            if (!configuredGroups.Contains(gatedGroup))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: operatingHours.gatedGroups contains '{Group}' but no configured tool exposes that operating group",
                    config.AgentId,
                    gatedGroup);
            }
        }

        var status = await ResolveStatusAsync(config.BusinessId, clockSnapshot, ct);
        if (status.IsOpen)
        {
            return new OperatingHoursTurnPolicyResult(
                configuredTools,
                new OperatingHoursTurnContext(true, false, gatedGroups, [], status.NextWindowText));
        }

        var blocked = configuredTools
            .Where(tool => tool.OperatingGroups.Any(group => gatedGroups.Contains(group, StringComparer.OrdinalIgnoreCase)))
            .Select(tool => tool.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (blocked.Count == 0)
        {
            return new OperatingHoursTurnPolicyResult(
                configuredTools,
                new OperatingHoursTurnContext(true, true, gatedGroups, [], status.NextWindowText));
        }

        var effectiveTools = configuredTools
            .Where(tool => !blocked.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new OperatingHoursTurnPolicyResult(
            effectiveTools,
            new OperatingHoursTurnContext(true, true, gatedGroups, blocked, status.NextWindowText));
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
                    return new OperatingHoursStatus(true, FormatWindow(window));

                if (offset == 0 && window.CloseTime <= nowTime)
                    continue;

                nextWindow ??= window;
            }
        }

        return new OperatingHoursStatus(false, nextWindow is null ? null : FormatWindow(nextWindow));
    }

    private static string FormatWindow(OperatingWindow window)
    {
        var day = window.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd d 'de' MMMM", SpanishCulture);
        return $"{day} de {FormatTime(window.OpenTime)} a {FormatTime(window.CloseTime)}";
    }

    private static string FormatTime(TimeSpan time) =>
        DateTime.Today.Add(time).ToString("h:mm tt", SpanishCulture).ToLowerInvariant();

    private sealed record OperatingHoursStatus(bool IsOpen, string? NextWindowText);

    private sealed record OperatingWindow(DateOnly Date, TimeSpan OpenTime, TimeSpan CloseTime);
}