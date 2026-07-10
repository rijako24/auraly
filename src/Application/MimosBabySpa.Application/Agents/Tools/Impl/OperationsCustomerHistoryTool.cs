using System.Text.Json;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("operations_get_customer_history")]
public sealed class OperationsCustomerHistoryTool : IAgentTool
{
private readonly IUnitOfWork _unitOfWork;

    public OperationsCustomerHistoryTool(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public string Name => "operations_get_customer_history";

    public string Description => "Returns recent orders, reservations, and last purchase information for a customer in the current business.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "customer_phone": { "type": "string" },
            "customer": { "type": "string", "description": "Name, phone, email, or document search" },
            "limit": { "type": "integer" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "customer_phone", out var customerPhone);
        ToolResultHelper.TryGetString(arguments, "customer", out var customer);

        var search = !string.IsNullOrWhiteSpace(customerPhone) ? customerPhone : customer;
        if (string.IsNullOrWhiteSpace(search))
            return ToolResultHelper.ErrorWithNextAction("customer_required", "Customer identifier is required.", "collect_customer_identifier", recoverable: true);

        var limit = ToolResultHelper.TryGetInt(arguments, "limit", out var rawLimit)
            ? Math.Clamp(rawLimit, 1, 20)
            : 5;

        var normalizedSearch = OperationsToolParsing.NormalizePhone(search);
        var hasSearchDigits = !string.IsNullOrWhiteSpace(normalizedSearch);
        var orderResult = await _unitOfWork.Orders.GetPagedByBusinessIdAsync(
            ctx.BusinessId,
            1,
            Math.Max(limit, 20),
            null,
            search,
            null,
            null,
            null,
            cancellationToken);

        var allReservations = (await _unitOfWork.Reservations.GetByBusinessIdAsync(ctx.BusinessId)).ToList();
        var reservations = allReservations
            .Where(r => MatchesCustomer(r.CustomerNameSnapshot, r.CustomerEmailSnapshot, r.CustomerPhoneSnapshot, search, normalizedSearch, hasSearchDigits))
            .OrderByDescending(r => r.ReservationDateTime ?? r.CreatedAt)
            .Take(limit)
            .ToList();

        var orders = orderResult.Items
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToList();

        var purchaseOrders = orderResult.Items
            .Where(o => o.Status is OrderStatus.Confirmed or OrderStatus.SyncPending or OrderStatus.Synced or OrderStatus.SyncFailed)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        var lastPurchase = purchaseOrders.FirstOrDefault();

        return ToolResultHelper.Ok(new
        {
            customer = search,
            last_purchase = lastPurchase is null ? null : new
            {
                order_id = lastPurchase.OrderId,
                date = lastPurchase.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                total = lastPurchase.Total,
                status = lastPurchase.Status.ToString(),
                customer_name = lastPurchase.CustomerNameSnapshot,
                customer_phone = lastPurchase.CustomerPhoneSnapshot
            },
            totals = new
            {
                orders_found = orderResult.TotalCount,
                purchase_orders = purchaseOrders.Count,
                purchase_amount = purchaseOrders.Sum(o => o.Total),
                reservations_found = reservations.Count
            },
            recent_orders = orders.Select(o => new
            {
                order_id = o.OrderId,
                date = o.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                total = o.Total,
                status = o.Status.ToString(),
                items = o.Items.Select(i => new { product = i.ProductNameSnapshot, quantity = i.Quantity, line_total = i.LineTotal }).ToList()
            }),
            recent_reservations = reservations.Select(r => new
            {
                reservation_id = r.ReservationId,
                date = r.ReservationDateTime?.ToString("yyyy-MM-dd"),
                time = r.ReservationDateTime?.ToString("HH:mm"),
                service = r.Service?.ServiceName,
                status = r.Status.ToString()
            })
        });
    }

    private static bool MatchesCustomer(string? name, string? email, string? phone, string search, string normalizedSearch, bool hasSearchDigits)
    {
        return (!string.IsNullOrWhiteSpace(name) && name.Contains(search, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(email) && email.Contains(search, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(phone) && (
                phone.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (hasSearchDigits && OperationsToolParsing.NormalizePhone(phone).Contains(normalizedSearch))));
    }
}
