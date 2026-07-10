using System.Text.Json;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("operations_get_business_metrics")]
public sealed class OperationsBusinessMetricsTool : IAgentTool
{
private readonly IUnitOfWork _unitOfWork;

    public OperationsBusinessMetricsTool(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public string Name => "operations_get_business_metrics";

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

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var startDate = OperationsToolParsing.TryGetDate(arguments, "date", ctx.BusinessToday, out var parsedStart)
            ? parsedStart
            : ctx.BusinessToday;
        var endDate = OperationsToolParsing.TryGetDate(arguments, "end_date", ctx.BusinessToday, out var parsedEnd)
            ? parsedEnd
            : startDate;

        if (endDate < startDate)
            return ToolResultHelper.Error("invalid_date_range", "end_date must be the same as or after date.");

        if (endDate.DayNumber - startDate.DayNumber > 366)
            return ToolResultHelper.Error("date_range_too_large", "Metrics are limited to 366 days.");

        var from = OperationsToolParsing.StartOfDay(startDate);
        var toExclusive = OperationsToolParsing.EndOfDayExclusive(endDate);
        var toInclusive = OperationsToolParsing.EndOfDayInclusive(endDate);

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

        return ToolResultHelper.Ok(new
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
