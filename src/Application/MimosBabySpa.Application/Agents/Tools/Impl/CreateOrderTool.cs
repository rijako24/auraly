using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class CreateOrderTool : IAgentTool
{
    private readonly ICommerceService _commerce;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReservationCreatedNotificationDispatcher _notificationDispatcher;
    private readonly IExternalEscalationService _externalEscalations;

    public CreateOrderTool(
        ICommerceService commerce,
        IUnitOfWork unitOfWork,
        IReservationCreatedNotificationDispatcher notificationDispatcher,
        IExternalEscalationService externalEscalations)
    {
        _commerce = commerce;
        _unitOfWork = unitOfWork;
        _notificationDispatcher = notificationDispatcher;
        _externalEscalations = externalEscalations;
    }

    public string Name => "create_order";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.OrderCreate];
    public string Description => "Creates the current order locally and, when configured, sends it to the commerce provider.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "customer_confirmed": { "type": "boolean" },
            "customer_name": { "type": "string" },
            "customer_email": { "type": "string" },
            "customer_phone": { "type": "string" },
            "customer_document": { "type": "string" },
            "delivery_address": { "type": "string" },
            "notes": { "type": "string" }
          },
          "required": ["customer_confirmed"]
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var confirmed = ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var c) && c;
        var order = await _commerce.CreateOrderAsync(
            ctx,
            new CreateOrderRequest(
                confirmed,
                Get(arguments, "customer_name"),
                Get(arguments, "customer_email"),
                Get(arguments, "customer_phone"),
                Get(arguments, "customer_document"),
                Get(arguments, "delivery_address"),
                Get(arguments, "notes")),
            cancellationToken);

        var isConfirmed = order.Status is OrderStatus.Confirmed or OrderStatus.SyncPending or OrderStatus.Synced;
        if (isConfirmed)
            await NotifyOrderCreatedAsync(ctx, order.OrderId, cancellationToken);

        return ToolResultHelper.Ok(new { order, is_order_confirmed = isConfirmed });
    }

    private async Task NotifyOrderCreatedAsync(AgentToolContext ctx, Guid orderId, CancellationToken ct)
    {
        if (ctx.Config is null)
            return;

        var entity = await _unitOfWork.Orders.GetByIdAsync(ctx.BusinessId, orderId, ct);
        if (entity is null)
            return;

        var items = await _unitOfWork.OrderItems.GetByOrderIdAsync(ctx.BusinessId, orderId, ct);
        var custom = BuildCustomPayload(entity, items);

        await _notificationDispatcher.SendEventAsync(ctx.BusinessId, ctx.Config, "order_created", custom, ct);
        await _externalEscalations.EscalateNextAsync(
            new ExternalEscalationRequest(ctx.Config.AgentId, "order_created", "order", orderId, custom),
            ct);
    }

    private static Dictionary<string, string> BuildCustomPayload(Order order, IReadOnlyList<OrderItem> items)
    {
        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_id"] = order.OrderId.ToString(),
            ["order_number"] = ShortId(order.OrderId),
            ["customer_name"] = order.CustomerNameSnapshot ?? string.Empty,
            ["customer_phone"] = order.CustomerPhoneSnapshot ?? string.Empty,
            ["customer_email"] = order.CustomerEmailSnapshot ?? string.Empty,
            ["delivery_address"] = order.DeliveryAddressSnapshot ?? string.Empty,
            ["notes"] = order.Notes ?? string.Empty,
            ["currency"] = order.Currency,
            ["subtotal"] = Money(order.Subtotal),
            ["total"] = Money(order.Total),
            ["items"] = string.Join("; ", items.Select(i => $"{i.ProductNameSnapshot} x{i.Quantity:N0}"))
        };

        TryReadOrderCustomAttributes(order.CustomAttributesJson, custom);
        return custom;
    }

    private static void TryReadOrderCustomAttributes(string? json, Dictionary<string, string> custom)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var name in new[] { "city", "shipping_cost" })
            {
                if (root.TryGetProperty(name, out var value))
                    custom[name] = value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : value.GetRawText();
            }

            if (root.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Object)
            {
                foreach (var fact in facts.EnumerateObject())
                {
                    if (fact.Value.ValueKind == JsonValueKind.String)
                        custom.TryAdd(fact.Name, fact.Value.GetString() ?? string.Empty);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string? Get(JsonElement args, string property) =>
        ToolResultHelper.TryGetString(args, property, out var value) ? value : null;

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string Money(decimal amount) => amount.ToString("N0", CultureInfo.InvariantCulture);
}