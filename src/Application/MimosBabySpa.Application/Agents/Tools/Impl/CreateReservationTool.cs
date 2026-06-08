using System.Text.Json;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class CreateReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly IReservationIntentBuilder _intentBuilder;
    private readonly IBusinessRuleEngine _rules;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IConversationLifecycleService _lifecycle;

    public CreateReservationTool(
        IReservationService reservations,
        IReservationIntentBuilder intentBuilder,
        IBusinessRuleEngine rules,
        IPaymentLifecycleService paymentLifecycle,
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IConversationLifecycleService lifecycle)
    {
        _reservations = reservations;
        _intentBuilder = intentBuilder;
        _rules = rules;
        _paymentLifecycle = paymentLifecycle;
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _lifecycle = lifecycle;
    }

    public string Name => "create_reservation";

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

        var idempotentResult = TryBuildIdempotentResult(ctx, service!, dateStr!, timeStr!, customerName!);
        if (idempotentResult is not null)
            return idempotentResult;

        var pendingPayment = ctx.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(ctx.ConversationId, cancellationToken);
        if (pendingPayment?.Status == PaymentTransactionStatus.Created)
        {
            return ToolResultHelper.Error(
                "payment_pending",
                "There is a pending checkout link for this conversation. Do not call create_reservation until the payment flow finishes or is abandoned.",
                "If the customer wants to cancel the pending checkout, call reset_flow_context with checkout_action=abandon.");
        }

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
                availability.AvailableTimeSlots.Count > 0
                    ? $"Available slots: {string.Join(", ", availability.AvailableTimeSlots)}"
                    : "Call check_availability for alternative times.");
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

        var response = await _reservations.CreateReservationAsync(
            new CreateReservationRequest(
                ctx.BusinessId, ctx.ConversationId,
                service!, date, time,
                customerName!, customerEmail,
                customerPhone!, attributes,
                intent.CustomAttributesJson),
            cancellationToken);

        ctx.ManageableReservations =
        [
            new Domain.Entities.Reservation
            {
                ReservationId = response.ReservationId,
                BusinessId = ctx.BusinessId,
                ConversationId = ctx.ConversationId,
                Status = ReservationStatus.Confirmed,
                ReservationDateTime = date.ToDateTime(time),
                CustomerNameSnapshot = customerName,
                CustomerPhoneSnapshot = customerPhone,
                CustomAttributesJson = intent.CustomAttributesJson
            }
        ];

        await _lifecycle.CloseAsync(
            ctx.ConversationId, ConversationCloseReasons.ReservationConfirmed, cancellationToken);

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
        return ToolResultHelper.Ok(new
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
        }, idempotentReplay ? [] : [ReservationCreated]);
    }

    private static string? Coalesce(JsonElement args, string property, string? fallback)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs))
            return fromArgs;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}
