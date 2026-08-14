using Auraly.Platform.Application.Agents.Operations.Support;
using System.Text.Json;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Agents.Operations.Internal;

public sealed class GetBusinessMetricsOperation : IAgentOperation
{
private readonly IUnitOfWork _unitOfWork;

    public GetBusinessMetricsOperation(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public OperationDescriptor Descriptor => new(Name, ParametersSchema, ["internal.metrics_loaded"], [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement arguments, OperationContext context, CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("internal.get_business_metrics requires a conversation session.");
        var json = await ExecuteCoreAsync(arguments, session, cancellationToken);
        return OperationJsonResult.Parse(json, "internal.metrics_loaded");
    }

    public string Name => "internal.get_business_metrics";

    public string Description => "Returns operational metrics for the current business: revenue, orders, reservations, and top services for a date range.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "YYYY-MM-DD, today/hoy, or tomorrow/manana. Defaults to today" },
            "end_date": { "type": "string", "description": "Optional YYYY-MM-DD end date" }
          }
        }
        """;

    private async Task<string> ExecuteCoreAsync(JsonElement arguments, AgentConversationContext ctx, CancellationToken cancellationToken = default)
    {
        var startDate = InternalOperationParsing.TryGetDate(arguments, "date", ctx.BusinessToday, out var parsedStart)
            ? parsedStart
            : ctx.BusinessToday;
        var endDate = InternalOperationParsing.TryGetDate(arguments, "end_date", ctx.BusinessToday, out var parsedEnd)
            ? parsedEnd
            : startDate;

        if (endDate < startDate)
            return OperationJsonHelper.Error("invalid_date_range", "end_date must be the same as or after date.");

        if (endDate.DayNumber - startDate.DayNumber > 366)
            return OperationJsonHelper.Error("date_range_too_large", "Metrics are limited to 366 days.");

        var from = InternalOperationParsing.StartOfDay(startDate);
        var toExclusive = InternalOperationParsing.EndOfDayExclusive(endDate);
        var toInclusive = InternalOperationParsing.EndOfDayInclusive(endDate);

        var revenue = await _unitOfWork.PaymentTransactions.GetTotalRevenueByBusinessIdAsync(
            ctx.BusinessId,
            from,
            toInclusive,
            cancellationToken);

        var orders = await _unitOfWork.Orders.GetSummaryByBusinessIdAsync(
            ctx.BusinessId,
            null,
            null,
            from,
            toExclusive.AddTicks(-1),
            null,
            cancellationToken);

        var reservations = (await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(ctx.BusinessId, from, toInclusive)).ToList();
        var topServices = await _unitOfWork.Reservations.GetTopServicesByBusinessIdAsync(
            ctx.BusinessId,
            5,
            from,
            toInclusive,
            cancellationToken);

        return OperationJsonHelper.Ok(new
        {
            date = startDate.ToString("yyyy-MM-dd"),
            end_date = endDate.ToString("yyyy-MM-dd"),
            revenue,
            orders = new
            {
                total = orders.TotalOrders,
                total_amount = orders.TotalAmount,
                draft = orders.DraftCount,
                awaiting_payment = orders.AwaitingPaymentCount,
                confirmed = orders.ConfirmedCount,
                synced = orders.SyncedCount,
                cancelled = orders.CancelledCount
            },
            reservations = new
            {
                total = reservations.Count,
                confirmed = reservations.Count(r => r.Status == ReservationStatus.Confirmed),
                completed = reservations.Count(r => r.Status == ReservationStatus.Completed),
                cancelled = reservations.Count(r => r.Status == ReservationStatus.Cancelled),
                on_hold = reservations.Count(r => r.Status == ReservationStatus.OnHold),
                pending = reservations.Count(r => r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.PendingCalendar)
            },
            top_services = topServices.Select(s => new
            {
                service_id = s.ServiceId,
                service = s.ServiceName,
                total_reservations = s.TotalReservations,
                estimated_revenue = s.Revenue
            })
        });
    }
}
