using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Creates a reservation for the current flow session.
/// Universal input keys: item, date, time, customer_name, customer_email, customer_phone, attributes
/// Business-specific attributes: clave "attributes" (p. ej. {{variables_group:business}}) y cualquier
/// otra clave del input_mapping que no sea universal (p. ej. selected_add_ons) se fusionan en BusinessAttributes.
/// Output keys: reservation_id (string), success (bool)
///
/// Uses ServiceNameResolver to guarantee the service name matches the canonical DB value
/// before creating the reservation record.
/// </summary>
public class CreateReservationAction : IFlowAction
{
    private readonly IReservationService _reservationService;
    private readonly ServiceNameResolver _nameResolver;
    private readonly ILogger<CreateReservationAction> _logger;

    public string ActionType => "create_reservation";

    public CreateReservationAction(
        IReservationService reservationService,
        ServiceNameResolver nameResolver,
        ILogger<CreateReservationAction> logger)
    {
        _reservationService = reservationService;
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
        var customerName = inputs.GetString("customer_name");
        var customerEmail = inputs.GetString("customer_email");
        var customerPhone = inputs.GetString("customer_phone");

        // Resolve extracted service name to canonical DB name (fuzzy match)
        if (!string.IsNullOrEmpty(item))
        {
            var resolvedItem = await _nameResolver.ResolveAsync(ctx.BusinessId, item, ct) ?? item;
            if (!string.Equals(resolvedItem, item, StringComparison.Ordinal))
                _logger.LogInformation("CreateReservation: resolved '{Raw}' → '{Canonical}'", item, resolvedItem);
            item = resolvedItem;
        }

        if (string.IsNullOrEmpty(item) || string.IsNullOrEmpty(date) ||
            string.IsNullOrEmpty(time) || string.IsNullOrEmpty(customerName))
        {
            _logger.LogWarning("CreateReservation: missing required inputs");
            return FlowActionResult.Failed("Missing required reservation data");
        }

        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(time, out var parsedTime))
        {
            _logger.LogWarning("CreateReservation: invalid date/time");
            return FlowActionResult.Failed("Invalid date or time format");
        }

        // Parse business-specific attributes from the "attributes" key (comma-separated key=value pairs)
        var attributesMutable = ParseAttributes(inputs.GetString("attributes"));

        // Also read any additional inputs passed directly (not universal keys) as attributes
        var universalKeys = new HashSet<string>(
            ["item", "date", "time", "customer_name", "customer_email", "customer_phone", "attributes"],
            StringComparer.OrdinalIgnoreCase);

        foreach (var kv in inputs.Where(k => !universalKeys.Contains(k.Key)))
            attributesMutable[kv.Key] = kv.Value?.ToString();

        IReadOnlyDictionary<string, string> attributes = attributesMutable
            .Where(kv => kv.Value != null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);

        try
        {
            var request = new CreateReservationRequest(
                BusinessId: ctx.BusinessId,
                ConversationId: ctx.ConversationId,
                ServiceName: item,
                Date: parsedDate,
                Time: parsedTime,
                CustomerName: customerName,
                Email: customerEmail,
                Phone: customerPhone,
                BusinessAttributes: attributes
            );

            var result = await _reservationService.CreateReservationAsync(request, ct);

            return FlowActionResult.Succeeded(new Dictionary<string, object?>
            {
                ["reservation_id"] = result.ReservationId.ToString(),
                ["success"] = "true"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateReservation failed");
            return FlowActionResult.Failed(ex.Message);
        }
    }

    private static Dictionary<string, string> ParseAttributes(string? raw)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx > 0)
            {
                var k = part[..idx].Trim();
                var v = part[(idx + 1)..].Trim();
                if (!string.IsNullOrEmpty(k))
                    result[k] = v;
            }
        }
        return result;
    }
}
