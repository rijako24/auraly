using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Verifica disponibilidad de horarios para un servicio en una fecha dada.
/// El LLM NUNCA debe inventar horarios — siempre llama esta tool.
/// </summary>
public sealed class CheckAvailabilityTool : IAgentTool
{
    private readonly IAvailabilityService _availability;

    public CheckAvailabilityTool(IAvailabilityService availability) =>
        _availability = availability;

    public string Name => "check_availability";

    public string Description =>
        "Checks available time slots for a service on a specific date. " +
        "Always call this before confirming any appointment time. " +
        "If time is provided, checks that specific slot. Otherwise returns all available slots for the day.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": {
              "type": "string",
              "description": "Exact service name from the catalog (e.g. 'Masaje Prenatal')"
            },
            "date": {
              "type": "string",
              "description": "Date in YYYY-MM-DD format (must be today or future)"
            },
            "time": {
              "type": "string",
              "description": "Optional specific time in HH:mm format (24h)"
            }
          },
          "required": ["service", "date"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "service", out var service))
            return ToolResultHelper.Error("invalid_args", "Parameter 'service' is required.");

        if (!ToolResultHelper.TryGetString(arguments, "date", out var dateStr))
            return ToolResultHelper.Error("invalid_args", "Parameter 'date' is required.");

        if (!DateTime.TryParse(dateStr, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.", "Use YYYY-MM-DD format.");

        if (date.Date < DateTime.UtcNow.Date)
            return ToolResultHelper.Error("past_date", "The date must be today or in the future.", "Ask the customer for a future date.");

        TimeSpan? time = null;
        if (ToolResultHelper.TryGetString(arguments, "time", out var timeStr) && !string.IsNullOrWhiteSpace(timeStr))
        {
            if (!TimeSpan.TryParse(timeStr, out var parsedTime))
                return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.", "Use HH:mm format.");
            time = parsedTime;
        }

        var result = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId, service, date, time, null, cancellationToken);

        return ToolResultHelper.Ok(new
        {
            is_available = result.IsAvailable,
            service = result.RequestServiceName,
            date = result.RequestDateString,
            time = result.RequestTimeString,
            available_slots = result.AvailableTimeSlots,
            message = result.ResponseMessage
        });
    }
}
