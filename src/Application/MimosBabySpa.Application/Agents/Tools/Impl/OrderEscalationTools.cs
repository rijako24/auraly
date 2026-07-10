using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal sealed class OrderEscalationResolver
{
    private static readonly Regex AttemptCodeRegex = new(@"\b[A-Z]{2,10}-\d{4,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IUnitOfWork _unitOfWork;

    public OrderEscalationResolver(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<OrderEscalation>> SearchAsync(
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
        else if (TryParseExternalInteractionPayload(ctx.InteractivePayload, out var payloadAttemptId, out _))
        {
            var byPayload = await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(payloadAttemptId, ct);
            if (BelongsToContactOrderEscalation(byPayload, ctx))
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
        var results = new List<OrderEscalation>();
        foreach (var attempt in attempts
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
            results.Add(new OrderEscalation(order, attempt, items, custom));
        }

        return results
            .OrderByDescending(a => a.Attempt.EscalatedAt)
            .Take(Math.Clamp(limit, 1, 20))
            .ToList();
    }

    public async Task<OrderEscalationResolution> ResolveOneAsync(
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
            return new OrderEscalationResolution("not_found", null, []);

        if (actionable.Count == 1)
            return new OrderEscalationResolution("resolved", actionable[0], matches);

        if (actionable.Count > 1)
            return new OrderEscalationResolution("ambiguous", null, actionable);

        return new OrderEscalationResolution("not_available", null, matches);
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

    private static bool BelongsToContactOrderEscalation(ExternalEscalationAttempt? attempt, AgentToolContext ctx) =>
        attempt is not null
        && attempt.BusinessId == ctx.BusinessId
        && attempt.ContactPhoneSnapshot.Equals(NormalizePhone(ctx.ChannelPhone), StringComparison.OrdinalIgnoreCase)
;

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

    internal static string? ReadInteractivePayloadOutcome(string? payload) =>
        TryParseExternalInteractionPayload(payload, out _, out var outcomeKey)
            ? outcomeKey
            : null;

    private static bool TryParseExternalInteractionPayload(string? payload, out Guid attemptId, out string? outcomeKey)
    {
        attemptId = Guid.Empty;
        outcomeKey = null;

        if (!InteractivePayloadParser.TryParse(payload, out var action)
            || !action.Scope.Equals("external_interaction", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Guid.TryParseExact(action.SourceId, "N", out attemptId) && !Guid.TryParse(action.SourceId, out attemptId))
            return false;

        outcomeKey = action.Outcome;
        return true;
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

internal sealed record OrderEscalation(
    Order Order,
    ExternalEscalationAttempt Attempt,
    IReadOnlyList<OrderItem> Items,
    IReadOnlyDictionary<string, string> CustomPayload);

internal sealed record OrderEscalationResolution(
    string Status,
    OrderEscalation? Assignment,
    IReadOnlyList<OrderEscalation> Matches);

internal static class OrderEscalationToolPayload
{
    public static object ToPayload(OrderEscalation assignment, DateTime now)
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
            request_code = attempt.AttemptCode,
            request_status = attempt.Status.ToString(),
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

[AgentToolMetadata("search_order")]
public sealed class SearchOrderTool : IAgentTool
{
private readonly OrderEscalationResolver _resolver;

    public SearchOrderTool(IUnitOfWork unitOfWork)
    {
        _resolver = new OrderEscalationResolver(unitOfWork);
    }

    public string Name => "search_order";

    public string Description =>
        "Searches order requests assigned to the current contact. Use it with PED codes, order numbers, customer data, or when multiple pending requests are possible.";

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
            orders = assignments.Select(a => OrderEscalationToolPayload.ToPayload(a, now)).ToList()
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

[AgentToolMetadata("accept_order_request")]
public sealed class AcceptOrderRequestTool : IAgentTool
{
private readonly OrderEscalationResolver _resolver;
    private readonly IExternalEscalationService _escalations;

    public AcceptOrderRequestTool(IUnitOfWork unitOfWork, IExternalEscalationService escalations)
    {
        _resolver = new OrderEscalationResolver(unitOfWork);
        _escalations = escalations;
    }

    public string Name => "accept_order_request";

    public string Description => "Accepts an order request assigned to the current contact.";

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
        AcceptAsync(arguments, ctx, cancellationToken);

    private async Task<string> AcceptAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken ct)
    {
        var payloadOutcome = OrderEscalationResolver.ReadInteractivePayloadOutcome(ctx.InteractivePayload);

                if (!string.IsNullOrWhiteSpace(payloadOutcome)
            && !payloadOutcome.Equals(ExternalEscalationOutcomeKeys.Accepted, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Ok(new
            {
                accepted = false,
                reason = "outcome_mismatch"
            });
        }

        var resolution = await _resolver.ResolveOneAsync(ctx, arguments, ct);
        var now = DateTime.UtcNow;
        if (resolution.Assignment is null)
            return ToolResultHelper.Ok(new
            {
                accepted = false,
                reason = resolution.Status,
                orders = resolution.Matches.Select(a => OrderEscalationToolPayload.ToPayload(a, now)).ToList()
            });

        var assignment = resolution.Assignment;
        if (assignment.Attempt.ExpiresAt <= now)
            return ToolResultHelper.Ok(new
            {
                accepted = false,
                reason = "expired",
                order = OrderEscalationToolPayload.ToPayload(assignment, now)
            });

        ToolResultHelper.TryGetString(arguments, "response_text", out var responseText);
        responseText = string.IsNullOrWhiteSpace(responseText) ? ctx.LatestUserMessage : responseText;
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_id"] = assignment.Order.OrderId.ToString(),
            ["order_number"] = assignment.CustomPayload.TryGetValue("order_number", out var orderNumber) ? orderNumber : assignment.Order.OrderId.ToString("N")[..8].ToUpperInvariant()
        };

        var result = await _escalations.CompleteAttemptAsync(
            new ExternalEscalationCompletionRequest(
                ctx.BusinessId,
                assignment.Attempt.ExternalEscalationAttemptId,
                ctx.ChannelPhone,
                ExternalEscalationOutcomeKeys.Accepted,
                ExternalEscalationAttemptStatus.Accepted,
                responseText,
                payload),
            ct);

        if (!result.Success)
            return ToolResultHelper.Ok(new
            {
                accepted = false,
                reason = "not_available",
                message = result.Message,
                order = OrderEscalationToolPayload.ToPayload(assignment, now)
            });

        return ToolResultHelper.Ok(new
        {
            accepted = true,
            order = OrderEscalationToolPayload.ToPayload(assignment with { Attempt = result.Attempt ?? assignment.Attempt }, DateTime.UtcNow),
            outcome_key = result.OutcomeKey
        }, ToolSideEffectNames.RequestCompleted);
    }
}

[AgentToolMetadata("reject_order_request")]
public sealed class RejectOrderRequestTool : IAgentTool
{
private readonly OrderEscalationResolver _resolver;
    private readonly IExternalEscalationService _escalations;

    public RejectOrderRequestTool(IUnitOfWork unitOfWork, IExternalEscalationService escalations)
    {
        _resolver = new OrderEscalationResolver(unitOfWork);
        _escalations = escalations;
    }

    public string Name => "reject_order_request";

    public string Description =>
        "Rejects an order request assigned to the current contact. " +
        "Do not ask for a rejection reason; a clear rejection is enough.";

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
        var payloadOutcome = OrderEscalationResolver.ReadInteractivePayloadOutcome(ctx.InteractivePayload);

                if (!string.IsNullOrWhiteSpace(payloadOutcome)
            && !payloadOutcome.Equals(ExternalEscalationOutcomeKeys.Declined, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Ok(new
            {
                rejected = false,
                reason = "outcome_mismatch"
            });
        }

        var resolution = await _resolver.ResolveOneAsync(ctx, arguments, cancellationToken);
        var now = DateTime.UtcNow;
        if (resolution.Assignment is null)
            return ToolResultHelper.Ok(new
            {
                rejected = false,
                reason = resolution.Status,
                orders = resolution.Matches.Select(a => OrderEscalationToolPayload.ToPayload(a, now)).ToList()
            });

        var assignment = resolution.Assignment;
        if (assignment.Attempt.ExpiresAt <= now)
            return ToolResultHelper.Ok(new
            {
                rejected = false,
                reason = "expired",
                order = OrderEscalationToolPayload.ToPayload(assignment, now)
            });

        ToolResultHelper.TryGetString(arguments, "response_text", out var responseText);
        responseText = string.IsNullOrWhiteSpace(responseText) ? ctx.LatestUserMessage : responseText;
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_id"] = assignment.Order.OrderId.ToString(),
            ["order_number"] = assignment.CustomPayload.TryGetValue("order_number", out var orderNumber) ? orderNumber : assignment.Order.OrderId.ToString("N")[..8].ToUpperInvariant()
        };

        var result = await _escalations.CompleteAttemptAsync(
            new ExternalEscalationCompletionRequest(
                ctx.BusinessId,
                assignment.Attempt.ExternalEscalationAttemptId,
                ctx.ChannelPhone,
                ExternalEscalationOutcomeKeys.Declined,
                ExternalEscalationAttemptStatus.Declined,
                responseText,
                payload),
            cancellationToken);

        if (!result.Success)
            return ToolResultHelper.Ok(new
            {
                rejected = false,
                reason = "not_available",
                message = result.Message,
                order = OrderEscalationToolPayload.ToPayload(assignment, now)
            });

        return ToolResultHelper.Ok(new
        {
            rejected = true,
            order = OrderEscalationToolPayload.ToPayload(assignment with { Attempt = result.Attempt ?? assignment.Attempt }, DateTime.UtcNow),
            outcome_key = result.OutcomeKey
        }, ToolSideEffectNames.RequestCompleted);
    }
}
