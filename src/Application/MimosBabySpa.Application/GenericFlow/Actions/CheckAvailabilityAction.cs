using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Checks slot availability for a given item/date/time.
///
/// Input keys (from input_mapping):
///   item — service name (fuzzy-matched to canonical DB name)
///   date — ISO date string (yyyy-MM-dd)
///   time — optional time string (HH:mm)
///
/// Input keys (from node config — passed as raw JSON by ActionNodeHandler):
///   scheduling — JSON object matching <see cref="AvailabilityParams"/>
///     Contains schedule, slotIntervalMinutes, bufferBetweenAppointmentsMinutes,
///     requireEmployee, employeeStrategy.
///
/// Output keys: slots (string), has_exact_match (bool), confirmed_time (string?)
/// </summary>
public class CheckAvailabilityAction : IFlowAction
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IAvailabilityService _availabilityService;
    private readonly ServiceNameResolver _nameResolver;
    private readonly ILogger<CheckAvailabilityAction> _logger;

    public string ActionType => "check_availability";

    public CheckAvailabilityAction(
        IAvailabilityService availabilityService,
        ServiceNameResolver nameResolver,
        ILogger<CheckAvailabilityAction> logger)
    {
        _availabilityService = availabilityService;
        _nameResolver = nameResolver;
        _logger = logger;
    }

    public async Task<FlowActionResult> ExecuteAsync(
        Dictionary<string, object?> inputs,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        var item = inputs.GetString("item");
        var date = inputs.GetString("date");
        var time = inputs.GetString("time");

        if (string.IsNullOrEmpty(item) || string.IsNullOrEmpty(date))
        {
            _logger.LogWarning("CheckAvailability: missing item or date");
            return FlowActionResult.Failed("item and date are required");
        }

        // Resolve extracted service name to canonical DB name (fuzzy match)
        var resolvedItem = await _nameResolver.ResolveAsync(ctx.BusinessId, item, ct) ?? item;
        if (!string.Equals(resolvedItem, item, StringComparison.Ordinal))
            _logger.LogInformation("CheckAvailability: resolved '{Raw}' → '{Canonical}'", item, resolvedItem);
        item = resolvedItem;

        if (!DateOnly.TryParse(date, out var parsedDate))
        {
            _logger.LogWarning("CheckAvailability: invalid date '{Date}'", date);
            return FlowActionResult.Failed($"Invalid date: {date}");
        }

        TimeSpan? parsedTime = null;
        if (!string.IsNullOrEmpty(time) && TimeOnly.TryParse(time, out var t))
            parsedTime = t.ToTimeSpan();

        // Read scheduling params from node config (forwarded as JSON string by ActionNodeHandler)
        var policy = ParseSchedulingPolicy(inputs.GetString("scheduling"));

        try
        {
            var checkDate = parsedDate.ToDateTime(TimeOnly.MinValue);
            var result = await _availabilityService.CheckAvailabilityAsync(
                ctx.BusinessId, item, checkDate, parsedTime, policy, ct);

            var slots = string.Join(", ", result.AvailableTimeSlots ?? []);
            // An exact match requires both a specific time to have been requested AND that
            // time to be available. When no time was given, the service returns available
            // slots for the day (IsAvailable=true) but there is no exact slot confirmed.
            var hasExact = result.IsAvailable && parsedTime.HasValue;

            return FlowActionResult.Succeeded(new Dictionary<string, object?>
            {
                ["slots"] = slots,
                ["has_exact_match"] = hasExact.ToString().ToLowerInvariant(),
                // confirmed_time is only set when the slot is actually confirmed.
                // This prevents desired_time from being kept with an unavailable value across retries.
                ["confirmed_time"] = hasExact && parsedTime.HasValue
                    ? TimeOnly.FromTimeSpan(parsedTime.Value).ToString("HH:mm")
                    : null
            }, outputPort: hasExact ? "success" : "failure");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckAvailability failed for item={Item} date={Date}", item, date);
            return FlowActionResult.Failed(ex.Message);
        }
    }

    private AvailabilityParams? ParseSchedulingPolicy(string? schedulingJson)
    {
        if (string.IsNullOrWhiteSpace(schedulingJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<AvailabilityParams>(schedulingJson, _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CheckAvailability: failed to parse 'scheduling' block — using defaults");
            return null;
        }
    }
}
