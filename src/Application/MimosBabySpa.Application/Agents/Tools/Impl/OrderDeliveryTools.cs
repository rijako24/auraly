using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal sealed class OrderDeliveryAssignmentResolver
{
    private static readonly Regex AttemptCodeRegex = new(@"\b[A-Z]{2,10}-\d{4,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IUnitOfWork _unitOfWork;

    public OrderDeliveryAssignmentResolver(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<OrderDeliveryAssignment>> SearchAsync(
        AgentToolContext ctx,
        string? query,
        bool includeCompleted,
        int limit,
        CancellationToken ct)
    {
        var attemptCode = ExtractAttemptCode(query);
        var attempts = new List<ExternalEscalationAttempt>();
        var skipQueryFilter = false;

        if (!string.IsNullOrWhiteSpace(attemptCode))
        {
            var byCode = await _unitOfWork.ExternalEscalationAttempts.GetLatestByAttemptCodeForContactAsync(
                ctx.BusinessId,
                attemptCode,
                ctx.ChannelPhone,
                ct);
            if (byCode is not null)
                attempts.Add(byCode);
            skipQueryFilter = true;
        }
        else if (TryParseExternalInteractionPayload(ctx.InteractivePayload, out var payloadAttemptId))
        {
            var byPayload = await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(payloadAttemptId, ct);
            if (BelongsToContactOrderAssignment(byPayload, ctx))
                attempts.Add(byPayload!);
            skipQueryFilter = true;
        }
        else if (!string.IsNullOrWhiteSpace(ctx.ReplyToProviderMessageId))
        {
            var byReply = await _unitOfWork.ExternalEscalationAttempts.GetByWhatsAppMessageIdAsync(
                ctx.BusinessId,
                ctx.ReplyToProviderMessageId,
                ctx.ChannelPhone,
                ct);
            if (byReply is not null)
                attempts.Add(byReply);
            skipQueryFilter = true;
        }
        else
        {
            attempts.AddRange(await _unitOfWork.ExternalEscalationAttempts.GetRecentByContactPhoneAsync(
                ctx.BusinessId,
                ctx.ChannelPhone,
                limit,
                includeCompleted,
                ct));
        }

        if (attempts.Count == 0)
            return [];

        var normalizedQuery = NormalizeQuery(query);
        var results = new List<OrderDeliveryAssignment>();
        foreach (var attempt in attempts
                     .Where(a => IsOrderAssignment(a))
                     .GroupBy(a => a.ExternalEscalationAttemptId)
                     .Select(g => g.First()))
        {
            var order = await _unitOfWork.Orders.GetByIdAsync(attempt.BusinessId, attempt.TargetId, ct);
            if (order is null)
                continue;

            var custom = ReadCustomPayload(attempt.CustomPayloadJson);
            if (!string.IsNullOrWhiteSpace(normalizedQuery)
                && !skipQueryFilter
                && string.IsNullOrWhiteSpace(attemptCode)
                && !MatchesQuery(order, attempt, custom, normalizedQuery))
            {
                continue;
            }

            var items = await _unitOfWork.OrderItems.GetByOrderIdAsync(order.BusinessId, order.OrderId, ct);
            results.Add(new OrderDeliveryAssignment(order, attempt, items, custom));
        }

        return results
            .OrderByDescending(a => a.Attempt.EscalatedAt)
            .Take(Math.Clamp(limit, 1, 20))
            .ToList();
    }

    public async Task<OrderDeliveryAssignmentResolution> ResolveOneAsync(
        AgentToolContext ctx,
        JsonElement arguments,
        CancellationToken ct)
    {
        var query = ReadQuery(arguments, ctx);
        var matches = await SearchAsync(ctx, query, includeCompleted: true, limit: 10, ct);
        var actionable = matches
            .Where(a => a.Attempt.Status == ExternalEscalationAttemptStatus.Pending)
            .ToList();

        if (matches.Count == 0)
            return new OrderDeliveryAssignmentResolution("not_found", null, []);

        if (actionable.Count == 1)
            return new OrderDeliveryAssignmentResolution("resolved", actionable[0], matches);

        if (actionable.Count > 1)
            return new OrderDeliveryAssignmentResolution("ambiguous", null, actionable);

        return new OrderDeliveryAssignmentResolution("not_available", null, matches);
    }

    private static string? ReadQuery(JsonElement arguments, AgentToolContext ctx)
    {
        foreach (var key in new[] { "attempt_code", "order_number", "order_id", "query" })
        {
            if (ToolResultHelper.TryGetString(arguments, key, out var value))
                return value;
        }

        return !string.IsNullOrWhiteSpace(ctx.LatestUserMessage)
            ? ctx.LatestUserMessage
            : ctx.ConversationState.LastUserMessage;
    }

    private static bool BelongsToContactOrderAssignment(ExternalEscalationAttempt? attempt, AgentToolContext ctx) =>
        attempt is not null
        && attempt.BusinessId == ctx.BusinessId
        && attempt.ContactPhoneSnapshot.Equals(NormalizePhone(ctx.ChannelPhone), StringComparison.OrdinalIgnoreCase)
        && IsOrderAssignment(attempt);

    private static bool IsOrderAssignment(ExternalEscalationAttempt attempt) =>
        attempt.TargetType.Equals("order", StringComparison.OrdinalIgnoreCase)
        && attempt.EventName.Equals("order_created", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesQuery(
        Order order,
        ExternalEscalationAttempt attempt,
        IReadOnlyDictionary<string, string> custom,
        string query)
    {
        var values = new[]
        {
            order.OrderId.ToString(),
            ShortId(order.OrderId),
            order.ExternalOrderId,
            order.ExternalDocumentNumber,
            order.CustomerNameSnapshot,
            order.CustomerPhoneSnapshot,
            order.DeliveryAddressSnapshot,
            attempt.AttemptCode,
            custom.TryGetValue("order_number", out var orderNumber) ? orderNumber : null,
            custom.TryGetValue("customer_name", out var customerName) ? customerName : null,
            custom.TryGetValue("customer_phone", out var customerPhone) ? customerPhone : null
        };

        return values.Any(value => !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractAttemptCode(string? value)
    {
        var match = AttemptCodeRegex.Match(value ?? string.Empty);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static bool TryParseExternalInteractionPayload(string? payload, out Guid attemptId)
    {
        attemptId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && parts[0].Equals("external_interaction", StringComparison.OrdinalIgnoreCase)
            && (Guid.TryParseExact(parts[2], "N", out attemptId) || Guid.TryParse(parts[2], out attemptId));
    }

    private static string NormalizeQuery(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizePhone(string phone) => new(phone.Where(char.IsDigit).ToArray());

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    internal static IReadOnlyDictionary<string, string> ReadCustomPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

internal sealed record OrderDeliveryAssignment(
    Order Order,
    ExternalEscalationAttempt Attempt,
    IReadOnlyList<OrderItem> Items,
    IReadOnlyDictionary<string, string> CustomPayload);

internal sealed record OrderDeliveryAssignmentResolution(
    string Status,
    OrderDeliveryAssignment? Assignment,
    IReadOnlyList<OrderDeliveryAssignment> Matches);

internal static class OrderDeliveryToolPayload
{
    public static object ToPayload(OrderDeliveryAssignment assignment, DateTime now)
    {
        var order = assignment.Order;
        var attempt = assignment.Attempt;
        var isExpired = attempt.ExpiresAt <= now;
        var canAct = attempt.Status == ExternalEscalationAttemptStatus.Pending && !isExpired;

        return new
        {
            order_id = order.OrderId,
            order_number = assignment.CustomPayload.TryGetValue("order_number", out var orderNumber)
                ? orderNumber
                : order.OrderId.ToString("N")[..8].ToUpperInvariant(),
            assignment_code = attempt.AttemptCode,
            assignment_status = attempt.Status.ToString(),
            delivery_assignment_status = order.DeliveryAssignmentStatus.ToString(),
            can_accept = canAct,
            can_reject = canAct,
            is_expired = isExpired,
            expires_at_utc = attempt.ExpiresAt,
            customer_name = order.CustomerNameSnapshot,
            customer_phone = order.CustomerPhoneSnapshot,
            delivery_address = order.DeliveryAddressSnapshot,
            city = assignment.CustomPayload.TryGetValue("city", out var city) ? city : string.Empty,
            items = assignment.Items.Count > 0
                ? string.Join("; ", assignment.Items.Select(i => $"{i.ProductNameSnapshot} x{i.Quantity:N0}"))
                : assignment.CustomPayload.TryGetValue("items", out var items) ? items : string.Empty,
            total = order.Total,
            currency = order.Currency
        };
    }
}

public sealed class SearchOrderTool : IAgentTool
{
    private readonly OrderDeliveryAssignmentResolver _resolver;

    public SearchOrderTool(IUnitOfWork unitOfWork)
    {
        _resolver = new OrderDeliveryAssignmentResolver(unitOfWork);
    }

    public string Name => "search_order";

    public string Description =>
        "Searches delivery orders assigned to the current delivery contact. Use it with PED codes, order numbers, customer data, or when multiple pending orders are possible.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" },
            "attempt_code": { "type": "string" },
            "order_number": { "type": "string" },
            "include_completed": { "type": "boolean" },
            "limit": { "type": "integer" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var query = ReadSearchQuery(arguments, ctx);
        ToolResultHelper.TryGetBool(arguments, "include_completed", out var includeCompleted);
        var limit = ToolResultHelper.TryGetInt(arguments, "limit", out var requestedLimit)
            ? Math.Clamp(requestedLimit, 1, 20)
            : 5;

        var assignments = await _resolver.SearchAsync(ctx, query, includeCompleted, limit, cancellationToken);
        var now = DateTime.UtcNow;
        return ToolResultHelper.Ok(new
        {
            count = assignments.Count,
            orders = assignments.Select(a => OrderDeliveryToolPayload.ToPayload(a, now)).ToList()
        });
    }

    private static string? ReadSearchQuery(JsonElement arguments, AgentToolContext ctx)
    {
        foreach (var key in new[] { "attempt_code", "order_number", "query" })
        {
            if (ToolResultHelper.TryGetString(arguments, key, out var value))
                return value;
        }

        return ctx.LatestUserMessage;
    }
}

public sealed class AcceptOrderDeliveryTool : IAgentTool
{
    private readonly OrderDeliveryAssignmentResolver _resolver;
    private readonly IExternalEscalationService _externalEscalations;

    public AcceptOrderDeliveryTool(IUnitOfWork unitOfWork, IExternalEscalationService externalEscalations)
    {
        _resolver = new OrderDeliveryAssignmentResolver(unitOfWork);
        _externalEscalations = externalEscalations;
    }

    public string Name => "accept_order_delivery";

    public string Description => "Accepts a delivery order assigned to the current delivery contact.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "attempt_code": { "type": "string" },
            "order_number": { "type": "string" },
            "order_id": { "type": "string" },
            "response_text": { "type": "string" }
          }
        }
        """;

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default) =>
        CompleteAsync(arguments, ctx, "accepted", cancellationToken);

    private async Task<string> CompleteAsync(JsonElement arguments, AgentToolContext ctx, string outcomeKey, CancellationToken ct)
    {
        var resolution = await _resolver.ResolveOneAsync(ctx, arguments, ct);
        var now = DateTime.UtcNow;
        if (resolution.Assignment is null)
            return ToolResultHelper.Ok(new
            {
                accepted = false,
                reason = resolution.Status,
                orders = resolution.Matches.Select(a => OrderDeliveryToolPayload.ToPayload(a, now)).ToList()
            });

        var assignment = resolution.Assignment;
        if (assignment.Attempt.ExpiresAt <= now)
            return ToolResultHelper.Ok(new
            {
                accepted = false,
                reason = "expired",
                order = OrderDeliveryToolPayload.ToPayload(assignment, now)
            });

        ToolResultHelper.TryGetString(arguments, "response_text", out var responseText);
        responseText = string.IsNullOrWhiteSpace(responseText) ? ctx.LatestUserMessage : responseText;
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_id"] = assignment.Order.OrderId.ToString(),
            ["order_number"] = assignment.CustomPayload.TryGetValue("order_number", out var orderNumber) ? orderNumber : assignment.Order.OrderId.ToString("N")[..8].ToUpperInvariant()
        };

        var result = await _externalEscalations.CompleteAsync(
            ctx.BusinessId,
            assignment.Attempt.ExternalEscalationAttemptId,
            ctx.ChannelPhone,
            outcomeKey,
            responseText,
            payload,
            ct);

        if (!result.Success)
            return ToolResultHelper.Ok(new
            {
                accepted = false,
                reason = "not_available",
                message = result.Message,
                order = OrderDeliveryToolPayload.ToPayload(assignment, now)
            });

        return ToolResultHelper.Ok(new
        {
            accepted = true,
            order = OrderDeliveryToolPayload.ToPayload(assignment, DateTime.UtcNow),
            outcome_key = result.OutcomeKey
        }, ToolSideEffectNames.RequestCompleted);
    }
}

public sealed class RejectOrderDeliveryTool : IAgentTool
{
    private readonly OrderDeliveryAssignmentResolver _resolver;
    private readonly IExternalEscalationService _externalEscalations;

    public RejectOrderDeliveryTool(IUnitOfWork unitOfWork, IExternalEscalationService externalEscalations)
    {
        _resolver = new OrderDeliveryAssignmentResolver(unitOfWork);
        _externalEscalations = externalEscalations;
    }

    public string Name => "reject_order_delivery";

    public string Description => "Rejects a delivery order assigned to the current delivery contact so it can be reassigned or escalated.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "attempt_code": { "type": "string" },
            "order_number": { "type": "string" },
            "order_id": { "type": "string" },
            "response_text": { "type": "string" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var resolution = await _resolver.ResolveOneAsync(ctx, arguments, cancellationToken);
        var now = DateTime.UtcNow;
        if (resolution.Assignment is null)
            return ToolResultHelper.Ok(new
            {
                rejected = false,
                reason = resolution.Status,
                orders = resolution.Matches.Select(a => OrderDeliveryToolPayload.ToPayload(a, now)).ToList()
            });

        var assignment = resolution.Assignment;
        if (assignment.Attempt.ExpiresAt <= now)
            return ToolResultHelper.Ok(new
            {
                rejected = false,
                reason = "expired",
                order = OrderDeliveryToolPayload.ToPayload(assignment, now)
            });

        ToolResultHelper.TryGetString(arguments, "response_text", out var responseText);
        responseText = string.IsNullOrWhiteSpace(responseText) ? ctx.LatestUserMessage : responseText;
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_id"] = assignment.Order.OrderId.ToString(),
            ["order_number"] = assignment.CustomPayload.TryGetValue("order_number", out var orderNumber) ? orderNumber : assignment.Order.OrderId.ToString("N")[..8].ToUpperInvariant()
        };

        var result = await _externalEscalations.CompleteAsync(
            ctx.BusinessId,
            assignment.Attempt.ExternalEscalationAttemptId,
            ctx.ChannelPhone,
            "declined",
            responseText,
            payload,
            cancellationToken);

        if (!result.Success)
            return ToolResultHelper.Ok(new
            {
                rejected = false,
                reason = "not_available",
                message = result.Message,
                order = OrderDeliveryToolPayload.ToPayload(assignment, now)
            });

        return ToolResultHelper.Ok(new
        {
            rejected = true,
            order = OrderDeliveryToolPayload.ToPayload(assignment, DateTime.UtcNow),
            outcome_key = result.OutcomeKey,
            next_contact_requested = result.EscalatedNext
        }, ToolSideEffectNames.RequestCompleted);
    }
}
