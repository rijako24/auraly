using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Runtime;

public interface IOperationEventContextResolver
{
    bool CanResolve(string eventName);

    Task<MessageSequenceContext> ResolveAsync(
        OperationEvent operationEvent,
        IReadOnlyDictionary<string, string> facts,
        CancellationToken cancellationToken = default);
}

public sealed class ReservationCreatedOperationEventContextResolver : IOperationEventContextResolver
{
    public bool CanResolve(string eventName) =>
        eventName.Equals("reservation_created", StringComparison.OrdinalIgnoreCase);

    public Task<MessageSequenceContext> ResolveAsync(
        OperationEvent operationEvent,
        IReadOnlyDictionary<string, string> facts,
        CancellationToken cancellationToken = default)
    {
        var custom = MergePayload(facts, operationEvent.Payload);
        var reservation = new Reservation
        {
            ReservationId = ReadGuid(operationEvent.Payload, "reservationId") ?? Guid.Empty,
            Status = Domain.Enums.ReservationStatus.Confirmed,
            ReservationDateTime = ReadDateTime(operationEvent.Payload),
            CustomerNameSnapshot = ReadString(operationEvent.Payload, "customerName"),
            CustomerPhoneSnapshot = ReadString(operationEvent.Payload, "customerPhone")
        };
        return Task.FromResult(new MessageSequenceContext
        {
            Reservation = reservation,
            Custom = custom
        });
    }

    private static Dictionary<string, string> MergePayload(
        IReadOnlyDictionary<string, string> facts,
        JsonElement payload)
    {
        var custom = new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase);
        if (payload.ValueKind != JsonValueKind.Object)
            return custom;
        foreach (var property in payload.EnumerateObject())
        {
            var value = ScalarText(property.Value);
            if (value is not null)
                custom[property.Name] = value;
        }
        return custom;
    }

    private static DateTime? ReadDateTime(JsonElement payload)
    {
        var dateText = ReadString(payload, "date");
        var timeText = ReadString(payload, "time");
        return DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && TimeOnly.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? date.ToDateTime(time)
            : null;
    }

    private static Guid? ReadGuid(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static string? ReadString(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
            ? ScalarText(value)
            : null;

    private static string? ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };
}

public sealed class OrderCreatedOperationEventContextResolver : IOperationEventContextResolver
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderCreatedOperationEventContextResolver(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public bool CanResolve(string eventName) =>
        eventName.Equals("order_created", StringComparison.OrdinalIgnoreCase);

    public async Task<MessageSequenceContext> ResolveAsync(
        OperationEvent operationEvent,
        IReadOnlyDictionary<string, string> facts,
        CancellationToken cancellationToken = default)
    {
        var custom = new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase);
        var orderId = ReadGuid(operationEvent.Payload, "orderId");
        var businessId = ReadGuid(operationEvent.Payload, "businessId");
        if (!orderId.HasValue || !businessId.HasValue)
            return new MessageSequenceContext { Custom = custom };

        var order = await _unitOfWork.Orders.GetByIdAsync(businessId.Value, orderId.Value, cancellationToken);
        if (order is null)
            return new MessageSequenceContext { Custom = custom };

        custom["order_id"] = order.OrderId.ToString();
        custom["order_number"] = order.ExternalDocumentNumber
            ?? order.ExternalOrderId
            ?? order.OrderId.ToString("N")[..8].ToUpperInvariant();
        custom["customer_name"] = order.CustomerNameSnapshot ?? string.Empty;
        custom["customer_phone"] = order.CustomerPhoneSnapshot ?? string.Empty;
        custom["delivery_address"] = order.DeliveryAddressSnapshot ?? string.Empty;
        custom["total"] = order.Total.ToString("0.##", CultureInfo.InvariantCulture);
        custom["currency"] = order.Currency;
        custom["items"] = string.Join(", ", order.Items.Select(item =>
            $"{item.ProductNameSnapshot} x{item.Quantity.ToString("0.##", CultureInfo.InvariantCulture)}"));
        return new MessageSequenceContext { Custom = custom };
    }

    private static Guid? ReadGuid(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}

public sealed record DeterministicTurnEffectRequest(
    Guid BusinessId,
    Guid ConversationId,
    AgentConfig Config,
    ConversationState ConversationState,
    IDictionary<string, string> Facts,
    DeterministicTurnResult TurnResult);

public sealed record DeterministicTurnEffectResult(
    IReadOnlyList<OutboundMessage> OutboundMessages,
    IReadOnlyList<string> DispatchedNotificationEvents,
    bool RequestContextCompleted);

public interface IDeterministicTurnEffectProcessor
{
    Task<DeterministicTurnEffectResult> ProcessAsync(
        DeterministicTurnEffectRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Materializes successful deterministic turn effects after operation execution. Notification
/// contexts and customer sequences are resolved before request-scoped facts are cleared.
/// </summary>
public sealed class DeterministicTurnEffectProcessor : IDeterministicTurnEffectProcessor
{
    private readonly IReadOnlyList<IOperationEventContextResolver> _contextResolvers;
    private readonly IEventNotificationDispatcher _notifications;
    private readonly IMessageSequenceResolver _sequences;
    private readonly IRequestContextService _requestContext;
    private readonly IMediaUrlResolver? _mediaUrls;

    public DeterministicTurnEffectProcessor(
        IEnumerable<IOperationEventContextResolver> contextResolvers,
        IEventNotificationDispatcher notifications,
        IMessageSequenceResolver sequences,
        IRequestContextService requestContext)
    {
        _contextResolvers = contextResolvers.ToList();
        _notifications = notifications;
        _sequences = sequences;
        _requestContext = requestContext;
    }

    public DeterministicTurnEffectProcessor(
        IEnumerable<IOperationEventContextResolver> contextResolvers,
        IEventNotificationDispatcher notifications,
        IMessageSequenceResolver sequences,
        IRequestContextService requestContext,
        IMediaUrlResolver mediaUrls)
        : this(contextResolvers, notifications, sequences, requestContext)
    {
        _mediaUrls = mediaUrls;
    }

    public async Task<DeterministicTurnEffectResult> ProcessAsync(
        DeterministicTurnEffectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.TurnResult.Success)
            return new DeterministicTurnEffectResult([], [], false);

        var factSnapshot = new Dictionary<string, string>(request.Facts, StringComparer.OrdinalIgnoreCase)
        {
            ["source_conversation_id"] = request.ConversationId.ToString()
        };
        var domainEvents = request.TurnResult.DomainEvents
            .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var eventNames = request.TurnResult.Events
            .Concat(domainEvents.Keys)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dispatched = new List<string>();
        MessageSequenceContext sequenceContext = new() { Custom = factSnapshot };

        foreach (var eventName in eventNames)
        {
            var context = domainEvents.TryGetValue(eventName, out var operationEvent)
                ? await ResolveContextAsync(operationEvent, factSnapshot, cancellationToken)
                : new MessageSequenceContext { Custom = factSnapshot };
            context = MergeCustom(context, factSnapshot);
            sequenceContext = context;
            if (request.Config.Notifications.TryGetValue(eventName, out var notification)
                && notification.Enabled)
            {
                await _notifications.SendEventAsync(
                    request.BusinessId,
                    request.Config,
                    eventName,
                    context,
                    cancellationToken);
                dispatched.Add(eventName);
            }
        }

        var outbound = new List<OutboundMessage>();
        if (_mediaUrls is not null)
        {
            foreach (var media in request.TurnResult.OperationEffects
                         .OfType<OutboundMediaOperationEffect>())
            {
                var mediaUrl = await _mediaUrls.ResolveAsync(
                    request.BusinessId,
                    media.MediaReference,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(mediaUrl))
                    outbound.Add(new OutboundMessage(
                        media.Caption,
                        mediaUrl,
                        media.MediaType,
                        media.Filename));
            }
        }
        foreach (var sequenceName in request.TurnResult.Sequences.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var messages = await _sequences.ResolveAsync(
                request.BusinessId,
                sequenceName,
                request.Config.MessageSequences,
                sequenceContext,
                cancellationToken);
            outbound.AddRange(messages);
        }

        if (request.TurnResult.RequestCompleted)
        {
            await _requestContext.CompleteAsync(
                request.ConversationId,
                request.Config,
                request.ConversationState,
                request.Facts,
                "request_completed",
                cancellationToken);
        }

        return new DeterministicTurnEffectResult(
            outbound,
            dispatched,
            request.TurnResult.RequestCompleted);
    }

    private async Task<MessageSequenceContext> ResolveContextAsync(
        OperationEvent operationEvent,
        IReadOnlyDictionary<string, string> facts,
        CancellationToken cancellationToken)
    {
        var resolver = _contextResolvers.FirstOrDefault(value => value.CanResolve(operationEvent.Name));
        return resolver is null
            ? new MessageSequenceContext { Custom = facts }
            : await resolver.ResolveAsync(operationEvent, facts, cancellationToken);
    }

    private static MessageSequenceContext MergeCustom(
        MessageSequenceContext context,
        IReadOnlyDictionary<string, string> facts)
    {
        var custom = new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in context.Custom)
            custom[key] = value;
        return context with { Custom = custom };
    }
}
