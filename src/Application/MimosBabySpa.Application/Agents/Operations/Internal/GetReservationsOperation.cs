using MimosBabySpa.Application.Agents.Operations.Support;
using System.Text.Json;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Operations.Internal;

public sealed class GetReservationsOperation : IAgentOperation
{
private readonly IUnitOfWork _unitOfWork;

    public GetReservationsOperation(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public OperationDescriptor Descriptor => new(Name, ParametersSchema, ["internal.reservations_loaded"], [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement arguments, OperationContext context, CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("internal.get_reservations requires a conversation session.");
        var json = await ExecuteCoreAsync(arguments, session, cancellationToken);
        return OperationJsonResult.Parse(json, "internal.reservations_loaded");
    }

    public string Name => "internal.get_reservations";

    public string Description => "Lists reservations for the current business by date or date range for internal operations contacts.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "YYYY-MM-DD, today/hoy, or tomorrow/manana" },
            "end_date": { "type": "string", "description": "Optional YYYY-MM-DD end date" },
            "status": { "type": "string", "description": "Optional reservation status" },
            "customer": { "type": "string", "description": "Optional customer name or phone filter" },
            "limit": { "type": "integer" }
          },
          "required": ["date"]
        }
        """;

    private async Task<string> ExecuteCoreAsync(JsonElement arguments, AgentConversationContext ctx, CancellationToken cancellationToken = default)
    {
        if (!InternalOperationParsing.TryGetDate(arguments, "date", ctx.BusinessToday, out var startDate))
            return OperationJsonHelper.Error("date_required", "date must be provided as YYYY-MM-DD, today/hoy, or tomorrow/manana.");

        var endDate = InternalOperationParsing.TryGetDate(arguments, "end_date", ctx.BusinessToday, out var parsedEnd)
            ? parsedEnd
            : startDate;

        if (endDate < startDate)
            return OperationJsonHelper.Error("invalid_date_range", "end_date must be the same as or after date.");

        if (endDate.DayNumber - startDate.DayNumber > 31)
            return OperationJsonHelper.Error("date_range_too_large", "Reservation lookups are limited to 31 days.");

        ReservationStatus? status = null;
        if (OperationJsonHelper.TryGetString(arguments, "status", out var statusRaw)
            && Enum.TryParse<ReservationStatus>(statusRaw, true, out var parsedStatus))
        {
            status = parsedStatus;
        }

        OperationJsonHelper.TryGetString(arguments, "customer", out var customer);
        var customerDigits = InternalOperationParsing.NormalizePhone(customer);
        var hasCustomerDigits = !string.IsNullOrWhiteSpace(customerDigits);
        var limit = OperationJsonHelper.TryGetInt(arguments, "limit", out var rawLimit)
            ? Math.Clamp(rawLimit, 1, 100)
            : 50;

        var reservations = (await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
                ctx.BusinessId,
                InternalOperationParsing.StartOfDay(startDate),
                InternalOperationParsing.EndOfDayInclusive(endDate)))
            .Where(r => !status.HasValue || r.Status == status.Value)
            .Where(r => string.IsNullOrWhiteSpace(customer)
                || (!string.IsNullOrWhiteSpace(r.CustomerNameSnapshot) && r.CustomerNameSnapshot.Contains(customer, StringComparison.OrdinalIgnoreCase))
                || (hasCustomerDigits && !string.IsNullOrWhiteSpace(r.CustomerPhoneSnapshot) && InternalOperationParsing.NormalizePhone(r.CustomerPhoneSnapshot).Contains(customerDigits)))
            .OrderBy(r => r.ReservationDateTime)
            .Take(limit)
            .ToList();

        return OperationJsonHelper.Ok(new
        {
            date = startDate.ToString("yyyy-MM-dd"),
            end_date = endDate.ToString("yyyy-MM-dd"),
            count = reservations.Count,
            reservations = reservations.Select(r => new
            {
                reservation_id = r.ReservationId,
                date = r.ReservationDateTime?.ToString("yyyy-MM-dd"),
                time = r.ReservationDateTime?.ToString("HH:mm"),
                service = r.Service?.ServiceName,
                employee = r.Employee?.Name,
                customer_name = r.CustomerNameSnapshot,
                customer_phone = r.CustomerPhoneSnapshot,
                status = r.Status.ToString(),
                duration_minutes = r.DurationMinutes
            })
        });
    }
}
