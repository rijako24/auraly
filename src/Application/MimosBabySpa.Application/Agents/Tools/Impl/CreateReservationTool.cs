using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Packs.Booking;
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

    public string PackId => BookingPackIds.Booking;

    public string Name => "create_reservation";

    public IReadOnlyList<RoleRequirement> RoleRequirements =>
    [
        new(FactRoles.BookingService),
        new(FactRoles.BookingDate),
        new(FactRoles.BookingTime),
        new(FactRoles.CustomerName),
        new(FactRoles.CustomerPhone),
        new(FactRoles.CustomerEmail, Required: false),
        new(FactRoles.BookingAddOns, Required: false)
    ];

    public IReadOnlyList<string> RequiredTemplateIds => [];

    public string Description =>
        "Creates a confirmed reservation from the current booking facts and customer confirmation flag. " +
        "Returns reservation_id, status, and a rendered confirmation summary token.";

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
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var ctx = invocation.Context;
        var booking = ctx.GetPackContext<IBookingPackContext>();

        var service = Coalesce(invocation.Arguments, "service", invocation.Get(FactRoles.BookingService));
        var dateStr = Coalesce(invocation.Arguments, "date", invocation.Get(FactRoles.BookingDate));
        var timeStr = Coalesce(invocation.Arguments, "time", invocation.Get(FactRoles.BookingTime));
        var customerName = Coalesce(invocation.Arguments, "customer_name",
            invocation.Get(FactRoles.CustomerName) ?? ctx.Conversation.CustomerName);
        var customerPhone = Coalesce(invocation.Arguments, "customer_phone",
            ConversationContactPhone.Resolve(ctx) ?? invocation.Get(FactRoles.CustomerPhone));
        var customerEmail = Coalesce(invocation.Arguments, "customer_email",
            invocation.Get(FactRoles.CustomerEmail) ?? ctx.Conversation.CustomerEmail);
        var addOns = Coalesce(invocation.Arguments, "add_ons", invocation.Get(FactRoles.BookingAddOns));

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

        if (!ToolResultHelper.TryGetBool(invocation.Arguments, "customer_confirmed", out var confirmed) || !confirmed)
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

        var idempotentResult = TryBuildIdempotentResult(ctx, booking, service!, dateStr!, timeStr!, customerName!);
        if (idempotentResult is not null)
            return idempotentResult;

        var policy = booking?.BookingPolicy;
        if (policy?.DepositRequired == true)
        {
            var hasConfirmedPayment =
                booking?.ActivePayment?.Status == PaymentTransactionStatus.Confirmed
                || await _paymentLifecycle.HasConfirmedDepositAsync(ctx.ConversationId, cancellationToken);

            if (!hasConfirmedPayment)
            {
                return ToolResultHelper.Error(
                    "payment_required",
                    "This reservation requires a confirmed advance payment. Do not call create_reservation — " +
                    "the customer pays via prepare_checkout and confirmation is sent automatically when Wompi validates the payment.",
                    "If the customer says they already paid, reassure them the confirmation will arrive shortly. " +
                    "Use verify_payment only after 3+ insistences to check status — it does not create the reservation.");
            }
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

        BookingPackContext.Replace(ctx, activeReservation: reservation);

        await _lifecycle.CloseAsync(
            ctx.ConversationId, ConversationCloseReasons.ReservationConfirmed, cancellationToken);

        return BuildSuccessResult(
            ctx,
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
        IBookingPackContext? booking,
        string service,
        string dateStr,
        string timeStr,
        string customerName)
    {
        var reservation = booking?.ActiveReservation;
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
            ctx,
            reservation.ReservationId,
            service,
            dateStr,
            timeStr,
            customerName,
            idempotentReplay: true);
    }

    private static string BuildSuccessResult(
        AgentToolContext ctx,
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
        object? templateData = null;
        if (DateOnly.TryParse(dateStr, out var date) && TimeOnly.TryParse(timeStr, out var time))
        {
            var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_name"] = customerName,
                ["service_name"] = service,
                ["date_formatted"] = date.ToString("dd/MM/yyyy"),
                ["time"] = time.ToString("HH:mm"),
                ["employee"] = employee,
                ["add_ons"] = addOnNames is { Count: > 0 } ? string.Join(", ", addOnNames) : null
            };

            foreach (var (key, value) in ctx.Facts)
            {
                if (!data.ContainsKey(key))
                    data[key] = value;
            }

            templateData = data;
        }

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservationId,
            service,
            date = dateStr,
            time = timeStr,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = true,
            employee,
            duration_minutes = durationMinutes,
            add_ons = addOnNames,
            template_id = "reservation_created",
            template_data = templateData,
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
