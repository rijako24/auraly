using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("create_reservation", Capabilities = new[] { ToolCapabilities.ReservationCreate })]
public sealed class CreateReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly IReservationIntentBuilder _intentBuilder;
    private readonly IBusinessRuleEngine _rules;
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly ILogger<CreateReservationTool> _logger;

    public CreateReservationTool(
        IReservationService reservations,
        IReservationIntentBuilder intentBuilder,
        IBusinessRuleEngine rules,
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        ILogger<CreateReservationTool> logger)
    {
        _reservations = reservations;
        _intentBuilder = intentBuilder;
        _rules = rules;
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _logger = logger;
    }

    public string Name => "create_reservation";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.ReservationCreate];

    public string Description =>
        "Creates a confirmed reservation from the current booking facts and customer confirmation flag. " +
        "Returns reservation_id, service, date, time, and status. Does not send customer-facing messages.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string" },
            "date": { "type": "string" },
            "time": { "type": "string" },
            "customer_name": { "type": "string" },
            "customer_phone": { "type": "string" },
            "customer_email": { "type": "string" },
            "add_ons": { "type": "string" },
            "customer_confirmed": { "type": "boolean" }
          },
          "required": ["customer_confirmed"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var service = Coalesce(arguments, "service", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service));
        var dateStr = Coalesce(arguments, "date", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredDate));
        var timeStr = Coalesce(arguments, "time", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime));
        var customerName = Coalesce(arguments, "customer_name",
            ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerName) ?? ctx.Conversation.CustomerName);
        var customerPhone = Coalesce(arguments, "customer_phone",
            ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone));
        var customerEmail = Coalesce(arguments, "customer_email",
            ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerEmail) ?? ctx.Conversation.CustomerEmail);
        var addOns = Coalesce(arguments, "add_ons", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.AddOns));

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(service)) missing.Add("service");
        if (string.IsNullOrWhiteSpace(dateStr)) missing.Add("date");
        if (string.IsNullOrWhiteSpace(timeStr)) missing.Add("time");
        if (string.IsNullOrWhiteSpace(customerName)) missing.Add("customer_name");
        if (string.IsNullOrWhiteSpace(customerPhone)) missing.Add("customer_phone");
        if (missing.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. missing]);

        if (!AgentDateRules.TryParseDate(dateStr!, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.");
        if (AgentDateRules.IsPastDate(date, ctx.BusinessToday))
            return ToolResultHelper.Error("past_date", "Reservation date must be today or in the future.");
        if (!TimeOnly.TryParse(timeStr, out var time))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.");

        if (!ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var confirmed) || !confirmed)
        {
            return ToolResultHelper.Ok(new
            {
                status = "pending_confirmation",
                summary = new
                {
                    service,
                    date = dateStr,
                    time = timeStr,
                    customer_name = customerName,
                    customer_phone = customerPhone,
                    add_ons = string.IsNullOrWhiteSpace(addOns) ? null : addOns
                }
            });
        }

        var activePayment = ctx.ActivePayment;
        if (activePayment?.Status == PaymentTransactionStatus.Created)
        {
            return ToolResultHelper.Error(
                "payment_required",
                "A payment link is pending for this reservation. Wait for payment confirmation before creating the reservation.",
                "Do not call create_reservation for paid checkout flows.");
        }

        if (activePayment?.Status == PaymentTransactionStatus.Confirmed && !activePayment.ReservationId.HasValue)
        {
            return ToolResultHelper.Error(
                "payment_fulfillment_pending",
                "Payment is confirmed but no reservation is linked yet. The payment fulfillment handler must create or link the reservation.",
                "Do not call create_reservation after payment confirmation.");
        }
        EnsureReservationFacts(ctx, service!, dateStr!, timeStr!, customerName!, customerPhone!, customerEmail);

        var idempotentResult = TryBuildIdempotentResult(ctx, service!, dateStr!, timeStr!, customerName!);
        if (idempotentResult is not null)
            return idempotentResult;

        var ruleResult = await _rules.ValidateReservationAsync(
            ctx.BusinessId, service!, date, time, cancellationToken);
        if (!ruleResult.IsValid)
        {
            return ToolResultHelper.Error("business_rule_violation",
                ruleResult.Reason ?? "Business rules prevent this reservation.");
        }

        var schedulingPolicy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var availability = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId,
            service!,
            date.ToDateTime(TimeOnly.MinValue),
            time.ToTimeSpan(),
            schedulingPolicy,
            cancellationToken);

        if (!availability.IsAvailable)
        {
            return ToolResultHelper.Error(
                "slot_unavailable",
                availability.ResponseMessage ?? "The selected time is not available.",
                availability.AvailableOptions.Count > 0
                    ? $"Available options: {string.Join(", ", availability.AvailableOptions.Select(o => $"{o.Start}-{o.End}"))}"
                    : null);
        }

        var intent = await _intentBuilder.BuildFromContextAsync(ctx, cancellationToken);
        if (intent is null)
        {
            return ToolResultHelper.Error(
                "invalid_booking_data",
                "Could not build reservation intent from collected facts.");
        }

        var attributes = string.IsNullOrWhiteSpace(addOns)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [ReservationBusinessAttributeKeys.SelectedAddOns] = addOns! };

        _logger.LogInformation(
            "AgentTool create_reservation requested BusinessId={BusinessId} AgentId={AgentId} ConversationId={ConversationId} Stage={Stage} ToolIteration={ToolIteration} ActivePaymentId={ActivePaymentId} ActivePaymentRef={ActivePaymentRef} Service={Service} Date={Date} Time={Time} CustomerPhone={CustomerPhone}",
            ctx.BusinessId,
            ctx.AgentId,
            ctx.ConversationId,
            ctx.Conversation.CurrentStageName ?? "(unknown)",
            ctx.CurrentToolIteration,
            ctx.ActivePayment?.PaymentTransactionId,
            ctx.ActivePayment?.PaymentReferenceId,
            service,
            date,
            time,
            customerPhone);
        var response = await _reservations.CreateReservationAsync(
            new CreateReservationRequest(
                ctx.BusinessId, ctx.ConversationId,
                service!, date, time,
                customerName!, customerEmail,
                customerPhone!, attributes,
                intent.CustomAttributesJson),
            cancellationToken);

        _logger.LogInformation(
            "AgentTool create_reservation created ReservationId={ReservationId} BusinessId={BusinessId} AgentId={AgentId} ConversationId={ConversationId} Service={Service} Date={Date} Time={Time} CustomerPhone={CustomerPhone}",
            response.ReservationId,
            ctx.BusinessId,
            ctx.AgentId,
            ctx.ConversationId,
            response.ServiceName ?? service,
            date,
            time,
            customerPhone);
        var reservation = new Domain.Entities.Reservation
        {
            ReservationId = response.ReservationId,
            BusinessId = ctx.BusinessId,
            ConversationId = ctx.ConversationId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = date.ToDateTime(time),
            CustomerNameSnapshot = customerName,
            CustomerPhoneSnapshot = customerPhone,
            CustomAttributesJson = intent.CustomAttributesJson
        };

        ctx.ManageableReservations = [reservation];
        ctx.NotificationContexts["reservation_created"] = new MessageSequenceContext { Reservation = reservation };

        return BuildSuccessResult(
            response.ReservationId,
            response.ServiceName ?? service!,
            dateStr!,
            timeStr!,
            customerName!,
            response.EmployeeName,
            response.DurationMinutes,
            response.AddOnNames);
    }

    private static string? TryBuildIdempotentResult(
        AgentToolContext ctx,
        string service,
        string dateStr,
        string timeStr,
        string customerName)
    {
        var reservation = ctx.SingleManageableReservation;
        if (reservation?.Status != ReservationStatus.Confirmed
            || !reservation.ReservationDateTime.HasValue)
        {
            return null;
        }

        var existingDate = DateOnly.FromDateTime(reservation.ReservationDateTime.Value);
        var existingTime = TimeOnly.FromDateTime(reservation.ReservationDateTime.Value);

        if (!DateOnly.TryParse(dateStr, out var requestedDate)
            || !TimeOnly.TryParse(timeStr, out var requestedTime)
            || existingDate != requestedDate
            || existingTime != requestedTime)
        {
            return null;
        }

        return BuildSuccessResult(
            reservation.ReservationId,
            service,
            dateStr,
            timeStr,
            customerName,
            idempotentReplay: true);
    }

    private static string BuildSuccessResult(
        Guid reservationId,
        string service,
        string dateStr,
        string timeStr,
        string customerName,
        string? employee = null,
        int? durationMinutes = null,
        IReadOnlyList<string>? addOnNames = null,
        bool idempotentReplay = false)
    {
        var effects = idempotentReplay
            ? Array.Empty<string>()
            : [ToolSideEffectNames.RequestCompleted];
        var events = idempotentReplay
            ? Array.Empty<string>()
            : ["reservation_created"];

        return ToolResultHelper.OkWithEvents(new
        {
            reservation_id = reservationId,
            service,
            date = dateStr,
            time = timeStr,
            customer_name = customerName,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = true,
            employee,
            duration_minutes = durationMinutes,
            add_ons = addOnNames,
            idempotent_replay = idempotentReplay
        }, effects, events);
    }

    private static void EnsureReservationFacts(
        AgentToolContext ctx,
        string service,
        string date,
        string time,
        string customerName,
        string customerPhone,
        string? customerEmail)
    {
        var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        SetIfMissing(ctx.Facts, roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service, service);
        SetIfMissing(ctx.Facts, roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate, date);
        SetIfMissing(ctx.Facts, roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime, time);
        SetIfMissing(ctx.Facts, roles.KeyByRole("customer.name") ?? ConversationFactKeys.CustomerName, customerName);
        SetIfMissing(ctx.Facts, roles.KeyByRole("customer.phone") ?? ConversationFactKeys.CustomerPhone, customerPhone);

        if (!string.IsNullOrWhiteSpace(customerEmail))
            SetIfMissing(ctx.Facts, roles.KeyByRole("customer.email") ?? ConversationFactKeys.CustomerEmail, customerEmail);
    }

    private static void SetIfMissing(IDictionary<string, string> facts, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return;

        if (!facts.TryGetValue(key, out var current) || string.IsNullOrWhiteSpace(current))
            facts[key] = value.Trim();
    }
    private static string? Coalesce(JsonElement args, string property, string? fallback)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs))
            return fromArgs;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}



