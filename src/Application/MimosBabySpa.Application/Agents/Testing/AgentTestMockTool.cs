using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Testing;

public sealed class AgentTestMockTool : IAgentTool
{
    private readonly AgentTestExecutionLog _log;
    private readonly IDictionary<string, string> _memoryFacts;

    public AgentTestMockTool(
        string name,
        string description,
        string parametersSchema,
        AgentTestExecutionLog log,
        IDictionary<string, string> memoryFacts,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyList<string>? operatingGroups = null)
    {
        Name = name;
        Description = description;
        ParametersSchema = parametersSchema;
        Capabilities = capabilities ?? [];
        OperatingGroups = operatingGroups ?? [];
        _log = log;
        _memoryFacts = memoryFacts;
    }

    public string Name { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public IReadOnlyList<string> OperatingGroups { get; }
    public string Description { get; }
    public string ParametersSchema { get; }

    public ToolAvailabilityResult Evaluate(AgentToolContext ctx, JsonElement arguments) =>
        new(true, null, null);

    public Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var result = Name.ToLowerInvariant() switch
        {
            "set_fact" => ExecuteSetFact(arguments, ctx),
            "resolve_service_selection" => ExecuteResolveServiceSelection(arguments, ctx),
            "reset_flow_context" => ExecuteResetFlowContext(arguments, ctx),
            "prepare_checkout" => ExecutePrepareCheckout(arguments, ctx),
            "prepare_order_checkout" => ExecutePrepareOrderCheckout(arguments, ctx),
            "create_reservation" => ExecuteCreateReservation(arguments, ctx),
            "assign_paid_slot" => ExecuteAssignPaidSlot(arguments, ctx),
            "escalate_to_human" => ExecuteEscalation(arguments, ctx),
            "verify_payment" => ExecuteVerifyPayment(arguments, ctx),
            "send_message_sequence" => ExecuteSendMessageSequence(arguments, ctx),
            "suspend_reservation" => ExecuteReservationMutation(arguments, "reservation_suspend_requested"),
            _ => ToolResultHelper.Error("test_tool_not_supported", $"Tool '{Name}' is not supported in test mode.")
        };

        _log.Add("tool_executed", Name, new
        {
            mocked = true,
            arguments = SafeJson(arguments),
            result = SafeJson(result)
        });

        return Task.FromResult(result);
    }

    private string ExecuteSetFact(JsonElement arguments, AgentToolContext ctx)
    {
        if (!TryGetString(arguments, "key", out var rawKey))
            return ToolResultHelper.MissingPrerequisites(["key"]);

        if (!arguments.TryGetProperty("value", out var valueElement)
            || !TryReadScalarValue(valueElement, out var value))
        {
            return ToolResultHelper.MissingPrerequisites(["value"]);
        }

        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var canonicalKey = roleIndex.NormalizeKey(rawKey.Trim());
        var key = FactKeyNormalizer.TryNormalizeKey(canonicalKey, out var normalizedKey)
            ? normalizedKey
            : canonicalKey;

        ctx.Facts[key] = value;
        _memoryFacts[key] = value;
        _log.Add("fact_set", Name, new Dictionary<string, object?>
        {
            ["key"] = key,
            ["value"] = value,
            ["persisted"] = false
        });

        return ToolResultHelper.Ok(new
        {
            key,
            value,
            persisted = false,
            test_mode = true
        });
    }

    private string ExecuteResolveServiceSelection(JsonElement arguments, AgentToolContext ctx)
    {
        if (!TryGetString(arguments, "text", out var text))
            return ToolResultHelper.MissingPrerequisites(["text"]);

        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var key = roleIndex.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        ctx.Facts[key] = text;
        _memoryFacts[key] = text;

        _log.Add("fact_set", Name, new Dictionary<string, object?>
        {
            ["key"] = key,
            ["value"] = text,
            ["persisted"] = false
        });

        return ToolResultHelper.Ok(new
        {
            selection_status = "resolved",
            service = text,
            key,
            storage = "fact",
            test_mode = true
        });
    }
    private string ExecuteResetFlowContext(JsonElement arguments, AgentToolContext ctx)
    {
        TryGetString(arguments, "reason", out var reason);
        var persistentKeys = (ctx.Config?.FactSchema ?? [])
            .Where(f => f.ShouldRememberAcrossRequests())
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cleared = ctx.Facts.Keys
            .Where(key => !persistentKeys.Contains(key))
            .ToList();

        foreach (var key in cleared)
        {
            ctx.Facts.Remove(key);
            _memoryFacts.Remove(key);
        }

        ctx.ConversationState.Verifications.Clear();
        ctx.ConversationState.StageFactSnapshots.Clear();
        ctx.ActivePayment = null;

        _log.Add("flow_context_reset", Name, new { reason, clearedFacts = cleared });

        return ToolResultHelper.Ok(new
        {
            reason,
            cleared_facts = cleared,
            preserved_facts = persistentKeys,
            test_mode = true
        });
    }

    private string ExecutePrepareCheckout(JsonElement arguments, AgentToolContext ctx)
    {
        TryGetString(arguments, "service", out var service);
        TryGetString(arguments, "add_ons", out var addOns);

        var paymentId = Guid.NewGuid();
        var link = $"https://checkout.test/{paymentId:N}";
        ctx.ActivePayment = new PaymentTransaction
        {
            PaymentTransactionId = paymentId,
            BusinessId = ctx.BusinessId,
            ConversationId = ctx.ConversationId,
            PaymentReferenceId = $"test_{paymentId:N}",
            LinkUrl = link,
            Status = PaymentTransactionStatus.Created,
            Source = PaymentTransactionSource.Automated,
            Currency = "COP",
            AmountInCents = 0,
            CreatedAt = DateTime.UtcNow
        };

        _log.Add("payment_link_requested", Name, new { service, addOns, link, persisted = false });

        return ToolResultHelper.Ok(new
        {
            checkout_token = (string?)null,
            checkout_kind = "Reservation",
            template_id = "test_checkout",
            payment_required = true,
            payment_transaction_id = paymentId,
            payment_link = link,
            link_url = link,
            is_booking_confirmed = false,
            test_mode = true
        });
    }

    private string ExecutePrepareOrderCheckout(JsonElement arguments, AgentToolContext ctx)
    {
        var paymentId = Guid.NewGuid();
        var link = $"https://checkout.test/order/{paymentId:N}";
        ctx.ActivePayment = new PaymentTransaction
        {
            PaymentTransactionId = paymentId,
            BusinessId = ctx.BusinessId,
            ConversationId = ctx.ConversationId,
            PaymentReferenceId = $"test_order_{paymentId:N}",
            LinkUrl = link,
            Status = PaymentTransactionStatus.Created,
            Source = PaymentTransactionSource.Automated,
            AmountInCents = 100000,
            Currency = "COP",
            CheckoutKind = CheckoutKind.Order,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _log.Add("order_payment_link_requested", Name, new { link, persisted = false });
        return ToolResultHelper.Ok(new
        {
            checkout_token = (string?)null,
            checkout_kind = "Order",
            template_id = "test_order_checkout",
            payment_required = true,
            payment_transaction_id = paymentId,
            payment_link = link,
            is_order_confirmed = false,
            test_mode = true
        });
    }

    private string ExecuteCreateReservation(JsonElement arguments, AgentToolContext ctx)
    {
        TryGetString(arguments, "service", out var service);
        TryGetString(arguments, "date", out var date);
        TryGetString(arguments, "time", out var time);
        TryGetString(arguments, "customer_name", out var customerName);
        TryGetString(arguments, "customer_phone", out var customerPhone);

        var reservationId = Guid.NewGuid();
        _log.Add("reservation_create_requested", Name, new
        {
            reservationId,
            service,
            date,
            time,
            customerName,
            customerPhone,
            persisted = false
        });

        var reservation = new Reservation
        {
            ReservationId = reservationId,
            BusinessId = ctx.BusinessId,
            ConversationId = ctx.ConversationId,
            Status = ReservationStatus.Confirmed,
            CustomerNameSnapshot = customerName,
            CustomerPhoneSnapshot = customerPhone
        };

        ctx.ManageableReservations = [reservation];
        ctx.NotificationContexts["reservation_created"] = new MessageSequenceContext { Reservation = reservation };

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservationId,
            service,
            date,
            time,
            customer_name = customerName,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = true,
            test_mode = true
        }, ToolSideEffectNames.RequestCompleted);
    }

    private string ExecuteAssignPaidSlot(JsonElement arguments, AgentToolContext ctx)
    {
        TryGetString(arguments, "date", out var date);
        TryGetString(arguments, "time", out var time);

        var reservationId = Guid.NewGuid();
        _log.Add("paid_slot_assign_requested", Name, new
        {
            reservationId,
            date,
            time,
            paymentTransactionId = ctx.ActivePayment?.PaymentTransactionId,
            persisted = false
        });

        var reservation = new Reservation
        {
            ReservationId = reservationId,
            BusinessId = ctx.BusinessId,
            ConversationId = ctx.ConversationId,
            Status = ReservationStatus.Confirmed,
            CustomerNameSnapshot = ctx.Facts.GetValueOrDefault(ConversationFactKeys.CustomerName),
            CustomerPhoneSnapshot = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone)
        };

        ctx.ManageableReservations = [reservation];
        ctx.NotificationContexts["reservation_created"] = new MessageSequenceContext { Reservation = reservation };

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservationId,
            payment_transaction_id = ctx.ActivePayment?.PaymentTransactionId,
            date,
            time,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = true,
            test_mode = true
        }, ToolSideEffectNames.RequestCompleted);
    }

    private string ExecuteEscalation(JsonElement arguments, AgentToolContext ctx)
    {
        TryGetString(arguments, "reason", out var reason);
        TryGetString(arguments, "last_user_message", out var lastUserMessage);

        _log.Add("human_escalation_requested", Name, new
        {
            reason,
            lastUserMessage,
            contacts = ctx.EscalationContacts,
            notified = false
        });

        return ToolResultHelper.Ok(new
        {
            escalated = true,
            reason,
            message = "Human escalation contacts would be notified in production; the bot remains active.",
            test_mode = true
        }, EscalatedToHuman);
    }

    private string ExecuteVerifyPayment(JsonElement arguments, AgentToolContext ctx)
    {
        TryGetString(arguments, "payment_reference_id", out var referenceId);
        referenceId = string.IsNullOrWhiteSpace(referenceId)
            ? ctx.ActivePayment?.PaymentReferenceId
            : referenceId;

        _log.Add("payment_status_lookup_requested", Name, new
        {
            paymentReferenceId = referenceId,
            providerCalled = false
        });

        return ToolResultHelper.Ok(new
        {
            status = "pending",
            is_approved = false,
            payment_reference_id = referenceId,
            message = "Payment provider lookup skipped in agent test mode.",
            test_mode = true
        });
    }

    private string ExecuteSendMessageSequence(JsonElement arguments, AgentToolContext ctx)
    {
        TryGetString(arguments, "sequence", out var sequence);
        _log.Add("message_sequence_requested", Name, new { sequence, sent = false });

        return ToolResultHelper.Ok(new
        {
            sequence,
            queued = 0,
            sent = false,
            test_mode = true
        });
    }

    private string ExecuteReservationMutation(JsonElement arguments, string eventType)
    {
        _log.Add(eventType, Name, new { arguments = SafeJson(arguments), persisted = false });
        return ToolResultHelper.Ok(new
        {
            accepted = true,
            test_mode = true,
            message = "Reservation mutation skipped in agent test mode."
        });
    }

    private static bool TryGetString(JsonElement args, string property, out string value)
    {
        value = string.Empty;
        if (!args.TryGetProperty(property, out var el))
            return false;

        value = el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : el.GetRawText();

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadScalarValue(JsonElement element, out string value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static object? SafeJson(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(element.GetRawText());
        }
        catch
        {
            return element.GetRawText();
        }
    }

    private static object? SafeJson(string rawJson)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(rawJson);
        }
        catch
        {
            return rawJson;
        }
    }
}
